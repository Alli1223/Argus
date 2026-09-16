using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Argus.Server.Features.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Infrastructure;

/// <summary>A server that looks for releases on a fake GitHub, with checks and downloads on demand.</summary>
public sealed class UpdatesFixture(PostgresFixture postgres) : ArgusAppFixture(postgres)
{
    public FakeGitHub GitHub { get; } = new();

    private readonly string _cache = Path.Combine(Path.GetTempPath(), $"argus-update-tests-{Guid.NewGuid():N}");

    /// <summary>The directory the server shares with the updater service.</summary>
    public string ServerUpdatesDirectory { get; } = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"argus-server-updates-{Guid.NewGuid():N}")).FullName;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Argus:Updates:ApiUrl"] = FakeGitHub.ApiUrl,
        ["Argus:Updates:Repository"] = FakeGitHub.Repository,
        ["Argus:Updates:CacheDirectory"] = _cache,
        ["Argus:Updates:ServerUpdatesDirectory"] = ServerUpdatesDirectory,
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient(ReleaseSource.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => GitHub);

    public Task<UpdateStatus.Snapshot> CheckAsync() =>
        WithScopeAsync(services => services.GetRequiredService<UpdateChecker>().CheckAsync(TestContext.Current.CancellationToken));

    /// <summary>Waits for the release's build for a runtime to be downloaded and checked.</summary>
    public Task<AgentPackage> PrepareAsync(string runtime) =>
        WithScopeAsync(services => services.GetRequiredService<AgentPackages>().PrepareAsync(
            services.GetRequiredService<UpdateStatus>().Current.Latest!, runtime));

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var directory in new[] { _cache, ServerUpdatesDirectory }.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>Serves one release, with agent builds and their checksums, the way GitHub does.</summary>
public sealed class FakeGitHub : HttpMessageHandler
{
    public const string ApiUrl = "https://api.github.test";
    public const string Repository = "argus/argus";

    private const string DownloadBase = "https://github.test/download/";

    /// <summary>The release served as the latest, or null for a repository without releases.</summary>
    public FakeRelease? Latest { get; set; }

    /// <summary>Answers every request with 503, as GitHub does during an outage.</summary>
    public bool Broken { get; set; }

    public sealed record FakeRelease(string Version, IReadOnlyDictionary<string, byte[]> Files, string? Checksums = null)
    {
        /// <summary>A release with builds for every runtime and a truthful SHA256SUMS.</summary>
        public static FakeRelease For(string version) => new(version, new Dictionary<string, byte[]>
        {
            ["argus-agent-linux-x64"] = Encoding.UTF8.GetBytes($"linux-x64 agent {version}"),
            ["argus-agent-linux-arm64"] = Encoding.UTF8.GetBytes($"linux-arm64 agent {version}"),
            ["argus-agent-win-x64.exe"] = Encoding.UTF8.GetBytes($"win-x64 agent {version}"),
        });

        public string ChecksumsText => Checksums ?? string.Concat(Files.Select(file =>
            $"{Convert.ToHexStringLower(SHA256.HashData(file.Value))}  {file.Key}\n"));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        if (Broken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        if (url == $"{ApiUrl}/repos/{Repository}/releases/latest")
        {
            return Task.FromResult(Latest is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(ReleaseJson(Latest)));
        }

        if (Latest is not null && url.StartsWith(DownloadBase, StringComparison.Ordinal))
        {
            var name = url[DownloadBase.Length..];
            var body = name == AgentPackages.ChecksumsAsset
                ? Encoding.UTF8.GetBytes(Latest.ChecksumsText)
                : Latest.Files.GetValueOrDefault(name);
            if (body is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static object ReleaseJson(FakeRelease release) => new
    {
        tag_name = $"v{release.Version}",
        name = $"Argus {release.Version}",
        body = "- Things got better.",
        html_url = $"https://github.test/argus/argus/releases/tag/v{release.Version}",
        published_at = "2026-09-16T12:00:00Z",
        assets = release.Files.Keys.Append(AgentPackages.ChecksumsAsset).Select(name => new
        {
            name,
            browser_download_url = DownloadBase + name,
            size = name == AgentPackages.ChecksumsAsset ? release.ChecksumsText.Length : release.Files[name].Length,
        }),
    };

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
