using System.Net;
using Argus.Server.Infrastructure;
using Argus.Server.Tests.Infrastructure;

namespace Argus.Server.Tests.WebApp;

/// <summary>A server with a (tiny, fake) built web app to serve.</summary>
public sealed class WebAppFixture(PostgresFixture postgres) : ArgusAppFixture(postgres)
{
    public const string Marker = "argus-test-app";

    private readonly string _root = CreateRoot();

    protected override IReadOnlyDictionary<string, string?> Settings =>
        new Dictionary<string, string?> { [WebAppHosting.RootSetting] = _root };

    private static string CreateRoot()
    {
        var root = Directory.CreateTempSubdirectory("argus-web-").FullName;
        File.WriteAllText(Path.Combine(root, "index.html"), $"<!doctype html><title>{Marker}</title>");
        File.WriteAllText(Path.Combine(root, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "assets", "index-3f9a2c.js"), "console.log('argus');");
        return root;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}

public sealed class WebAppHostingTests(WebAppFixture app) : IClassFixture<WebAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/hosts/0199a0a2-0000-7000-8000-000000000001")]
    [InlineData("/alerts?severity=Critical")]
    public async Task Page_urls_get_the_app_and_are_never_cached(string path)
    {
        var response = await app.Factory.CreateClient().GetAsync(path, Ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Contains(WebAppFixture.Marker, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Fingerprinted_assets_are_cached_for_good()
    {
        var response = await app.Factory.CreateClient().GetAsync("/assets/index-3f9a2c.js", Ct);

        response.EnsureSuccessStatusCode();
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Other_files_are_revalidated()
    {
        var response = await app.Factory.CreateClient().GetAsync("/favicon.svg", Ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/api/does-not-exist")]
    [InlineData("/hubs/unknown")]
    [InlineData("/downloads/nothing")]
    public async Task Unknown_server_paths_stay_not_found(string path)
    {
        var response = await app.Factory.CreateClient().GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(WebAppFixture.Marker, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Missing_files_are_not_answered_with_the_app()
    {
        var response = await app.Factory.CreateClient().GetAsync("/missing.png", Ct);

        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain(WebAppFixture.Marker, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Install_scripts_are_still_downloadable()
    {
        var response = await app.Factory.CreateClient().GetAsync("/downloads/install.sh", Ct);

        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain(WebAppFixture.Marker, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Only_reads_get_the_app()
    {
        var response = await app.CreateClient().PostAsync("/hosts", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_server_still_answers_its_own_paths()
    {
        (await app.Factory.CreateClient().GetAsync("/health/live", Ct)).EnsureSuccessStatusCode();
        (await app.Factory.CreateClient().GetAsync("/api/info", Ct)).EnsureSuccessStatusCode();
    }
}

public sealed class WithoutWebAppTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Fact]
    public async Task Without_a_build_the_server_answers_only_its_own_paths()
    {
        var response = await app.Factory.CreateClient().GetAsync("/", TestContext.Current.CancellationToken);

        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
