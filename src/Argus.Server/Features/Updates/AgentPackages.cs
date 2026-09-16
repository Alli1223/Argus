using System.Security.Cryptography;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Updates;

/// <summary>An agent build from a release, downloaded, checked against the release's checksums and kept on disk.</summary>
public sealed record AgentPackage(string Runtime, string Version, string FilePath, string Sha256, long Size);

/// <summary>
/// Fetches agent builds from releases for agents to update from. A build is only kept when it matches
/// the release's SHA256SUMS. Everyone asking for the same build at once shares one download, and a
/// failed download is tried again after a while.
/// </summary>
public sealed class AgentPackages(ReleaseSource releases, IOptions<UpdateOptions> options, TimeProvider time, ILogger<AgentPackages> logger)
{
    public const string ChecksumsAsset = "SHA256SUMS";

    private const long MaxBuildBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(10);

    /// <summary>The release file of each runtime's build, as build/package-agent.sh names them.</summary>
    private static readonly Dictionary<string, string> AssetNames = new()
    {
        ["linux-x64"] = "argus-agent-linux-x64",
        ["linux-arm64"] = "argus-agent-linux-arm64",
        ["win-x64"] = "argus-agent-win-x64.exe",
    };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Download> _downloads = [];

    private sealed record Download(Task<AgentPackage> Task, DateTimeOffset StartedAt);

    public string CacheDirectory => string.IsNullOrWhiteSpace(options.Value.CacheDirectory)
        ? Path.Combine(Path.GetTempPath(), "argus-agent-updates")
        : options.Value.CacheDirectory;

    /// <summary>The runtime an agent on this platform runs, or null where releases have no build for it.</summary>
    public static string? RuntimeOf(HostPlatform platform, string architecture) => (platform, architecture.ToLowerInvariant()) switch
    {
        (HostPlatform.Linux, "x64") => "linux-x64",
        (HostPlatform.Linux, "arm64") => "linux-arm64",
        (HostPlatform.Windows, "x64") => "win-x64",
        _ => null,
    };

    public static bool HasBuild(ReleaseInfo release, string runtime) =>
        AssetNames.TryGetValue(runtime, out var asset) && release.FindAsset(asset) is not null;

    /// <summary>
    /// The runtime's build from the release, once downloaded and checked. The download starts on the first
    /// call; a failed one starts again when <paramref name="retryFailed"/> is set or ten minutes have passed.
    /// </summary>
    public Task<AgentPackage> PrepareAsync(ReleaseInfo release, string runtime, bool retryFailed = false)
    {
        var key = $"{release.Version}/{runtime}";
        lock (_gate)
        {
            if (_downloads.TryGetValue(key, out var existing) && !ShouldRestart(existing, retryFailed))
            {
                return existing.Task;
            }

            var download = new Download(Task.Run(() => DownloadAsync(release, runtime)), time.GetUtcNow());
            _downloads[key] = download;
            return download.Task;
        }
    }

    private bool ShouldRestart(Download download, bool retryFailed) => download.Task switch
    {
        { IsFaulted: true } => retryFailed || time.GetUtcNow() - download.StartedAt > RetryFailedAfter,
        { IsCompletedSuccessfully: true } => !File.Exists(download.Task.Result.FilePath),
        _ => false,
    };

    private async Task<AgentPackage> DownloadAsync(ReleaseInfo release, string runtime)
    {
        var assetName = AssetNames[runtime];
        var asset = release.FindAsset(assetName)
            ?? throw new InvalidOperationException($"Release {release.Tag} has no {assetName}.");
        var checksums = release.FindAsset(ChecksumsAsset)
            ?? throw new InvalidOperationException($"Release {release.Tag} has no {ChecksumsAsset}, so its agents cannot be checked.");

        string? expected;
        using (var response = await releases.DownloadAsync(checksums.DownloadUrl, CancellationToken.None))
        {
            expected = ParseChecksums(await response.Content.ReadAsStringAsync()).GetValueOrDefault(assetName);
        }

        if (expected is null)
        {
            throw new InvalidDataException($"{ChecksumsAsset} in release {release.Tag} does not list {assetName}.");
        }

        var directory = Path.Combine(CacheDirectory, release.Version);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetName);
        var partial = $"{path}.{Guid.NewGuid():N}.part";
        try
        {
            string actual;
            long size = 0;
            using (var response = await releases.DownloadAsync(asset.DownloadUrl, CancellationToken.None))
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(partial))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81_920];
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    size += read;
                    if (size > MaxBuildBytes)
                    {
                        throw new InvalidDataException($"{assetName} in release {release.Tag} is larger than any agent build should be.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read));
                }

                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{assetName} from release {release.Tag} does not match {ChecksumsAsset}, so it was not used.");
            }

            File.Move(partial, path, overwrite: true);
            logger.LogInformation("Fetched agent {Version} for {Runtime} from release {Tag}", release.Version, runtime, release.Tag);
            RemoveOtherVersions(release.Version);
            return new AgentPackage(runtime, release.Version, path, actual, size);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fetching agent {Version} for {Runtime} failed", release.Version, runtime);
            throw;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>Reads sha256sum output: a hash, whitespace, then the file name (with a * in binary mode).</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts is [{ Length: 64 } hash, var name])
            {
                sums[name.TrimStart('*')] = hash.ToLowerInvariant();
            }
        }

        return sums;
    }

    private void RemoveOtherVersions(string version)
    {
        lock (_gate)
        {
            foreach (var key in _downloads.Keys.Where(key => !key.StartsWith(version + "/", StringComparison.Ordinal)).ToList())
            {
                _downloads.Remove(key);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(CacheDirectory).Where(directory => Path.GetFileName(directory) != version))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogDebug(ex, "Could not remove old agent builds in {Directory}", directory);
            }
        }
    }
}
