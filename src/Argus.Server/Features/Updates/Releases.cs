using System.Net;
using System.Text.Json;
using Argus.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Updates;

/// <summary>Release versions such as "0.3.0", tagged "v0.3.0", perhaps with a suffix like "-beta" or "+build".</summary>
public static class ReleaseVersions
{
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var core = text.Trim().TrimStart('v', 'V');
        var suffix = core.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            core = core[..suffix];
        }

        // "1.2" and "1.2.0" are the same version.
        return Version.TryParse(core, out var version) ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) : null;
    }

    /// <summary>Whether <paramref name="candidate"/> is later than <paramref name="current"/>. An unreadable current version counts as older.</summary>
    public static bool IsNewer(string? candidate, string? current) =>
        Parse(candidate) is { } next && (Parse(current) is not { } now || next > now);
}

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

public sealed record ReleaseInfo(
    string Version,
    string Tag,
    string Name,
    string Notes,
    Uri Url,
    DateTimeOffset PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? FindAsset(string name) => Assets.FirstOrDefault(asset => asset.Name == name);
}

/// <summary>Reads releases and their files from GitHub.</summary>
public sealed class ReleaseSource(IHttpClientFactory clients, IOptions<UpdateOptions> options)
{
    public const string HttpClientName = "github";

    /// <summary>The latest published release (drafts and pre-releases never are), or null when there is none.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{settings.ApiUrl.TrimEnd('/')}/repos/{settings.Repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.", inner: null, response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var release = json.RootElement;

        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var version = ReleaseVersions.Parse(tag)
            ?? throw new FormatException($"The latest release is tagged '{tag}', which is not a version.");

        return new ReleaseInfo(
            version.ToString(3),
            tag,
            OptionalString(release, "name") is { Length: > 0 } name ? name : tag,
            OptionalString(release, "body") ?? "",
            new Uri(release.GetProperty("html_url").GetString()!),
            release.GetProperty("published_at").GetDateTimeOffset(),
            [.. release.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString()!,
                new Uri(asset.GetProperty("browser_download_url").GetString()!),
                asset.GetProperty("size").GetInt64()))]);
    }

    /// <summary>Starts downloading a release file; the caller reads and disposes the response.</summary>
    public async Task<HttpResponseMessage> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        var response = await clients.CreateClient(HttpClientName)
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"Downloading {url} failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        return response;
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>What the latest look at GitHub found. The last release found is kept when a later check fails.</summary>
public sealed class UpdateStatus
{
    private Snapshot _current = new(null, null, null);

    public sealed record Snapshot(ReleaseInfo? Latest, DateTimeOffset? CheckedAt, string? Error);

    public Snapshot Current => Volatile.Read(ref _current);

    internal void Record(Snapshot snapshot) => Volatile.Write(ref _current, snapshot);
}

public sealed class UpdateChecker(ReleaseSource releases, UpdateStatus status, TimeProvider time, ILogger<UpdateChecker> logger)
{
    public async Task<UpdateStatus.Snapshot> CheckAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        try
        {
            var latest = await releases.GetLatestAsync(cancellationToken);
            if (latest is not null
                && ReleaseVersions.IsNewer(latest.Version, ServerVersion.Current)
                && status.Current.Latest?.Version != latest.Version)
            {
                logger.LogInformation("Argus {Version} is available; this server runs {Current}. {Url}",
                    latest.Version, ServerVersion.Current, latest.Url);
            }

            status.Record(new UpdateStatus.Snapshot(latest, now, null));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Checking GitHub for Argus releases failed");
            status.Record(status.Current with { CheckedAt = now, Error = ex.Message });
        }

        return status.Current;
    }
}

/// <summary>Checks for releases when the server starts and every few hours after.</summary>
internal sealed class UpdateCheckService(
    IServiceScopeFactory scopes,
    IOptions<UpdateOptions> options,
    TimeProvider time,
    ILogger<UpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CheckForUpdates || !options.Value.BackgroundChecks)
        {
            logger.LogInformation("Checking for Argus releases in the background is switched off");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.Value.CheckIntervalHours), time);
        try
        {
            do
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<UpdateChecker>().CheckAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
