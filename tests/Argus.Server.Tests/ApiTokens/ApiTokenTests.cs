using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Argus.Server.Features.ApiTokens;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Argus.Server.Infrastructure;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.ApiTokens;

public sealed class ApiTokenTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static async Task<CreatedApiToken> CreateTokenAsync(HttpClient client, string name = "Home Assistant")
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/account/api-tokens", new { name }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedApiToken>(ct))!;
    }

    /// <summary>A client like another program would use: the token and nothing else, no cookies.</summary>
    private HttpClient TokenClient(string token)
    {
        var client = app.Factory.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Tokens_are_shown_once_and_listed_without_the_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await app.CreateOwnerAsync("sam@example.com");

        var created = await CreateTokenAsync(owner);

        Assert.True(SecretTokens.LooksValid(created.Token, ApiTokenDefaults.Prefix));
        Assert.True(created.Summary.IsActive);

        var json = await owner.GetStringAsync("/api/account/api-tokens", ct);
        Assert.DoesNotContain(created.Token, json, StringComparison.Ordinal);
        var summary = Assert.Single(JsonSerializer.Deserialize<List<ApiTokenSummary>>(json, JsonSerializerOptions.Web)!);
        Assert.Equal("Home Assistant", summary.Name);
        Assert.StartsWith(summary.TokenPrefix.TrimEnd('…'), created.Token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_reads_what_its_owner_sees_and_records_its_use()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await app.CreateOwnerAsync("tara@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "machine-tara", "tara-pc");
        var other = await app.CreateOwnerAsync("uma@example.com");
        await app.RegisterHostAsync(other, "machine-uma", "uma-pc");
        var token = TokenClient((await CreateTokenAsync(owner)).Token);

        var me = await token.GetJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal("tara@example.com", me!.Email);

        var hosts = await token.GetJsonAsync<List<HostSummary>>("/api/hosts");
        Assert.Equal(hostId, Assert.Single(hosts!).Id);

        var summary = Assert.Single((await owner.GetFromJsonAsync<List<ApiTokenSummary>>("/api/account/api-tokens", ct))!);
        Assert.NotNull(summary.LastUsedAt);
    }

    [Fact]
    public async Task Tokens_only_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await app.CreateOwnerAsync("vic@example.com");
        var token = TokenClient((await CreateTokenAsync(owner)).Token);
        token.DefaultRequestHeaders.Add("X-Argus-Csrf", "1");

        var making = await token.PostAsJsonAsync("/api/account/api-tokens", new { name = "another" }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, making.StatusCode);
        Assert.Contains("read-only", await making.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await token.PostAsJsonAsync("/api/enrollment-tokens", new { name = "Servers" }, ct)).StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<List<ApiTokenSummary>>("/api/account/api-tokens", ct))!);
    }

    [Fact]
    public async Task Tokens_carry_their_owners_role()
    {
        var ct = TestContext.Current.CancellationToken;
        var admin = TokenClient((await CreateTokenAsync(await app.CreateOwnerAsync("wren@example.com", Roles.Admin))).Token);
        var user = TokenClient((await CreateTokenAsync(await app.CreateOwnerAsync("xavi@example.com"))).Token);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users", ct)).StatusCode);
    }

    [Fact]
    public async Task Revoked_and_unknown_tokens_are_refused_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await app.CreateOwnerAsync("yara@example.com");
        var created = await CreateTokenAsync(owner);
        var token = TokenClient(created.Token);
        Assert.Equal(HttpStatusCode.OK, (await token.GetAsync("/api/hosts", ct)).StatusCode);

        var stranger = await app.CreateOwnerAsync("zane@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/account/api-tokens/{created.Summary.Id}", ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/account/api-tokens/{created.Summary.Id}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await token.GetAsync("/api/hosts", ct)).StatusCode);

        var forged = TokenClient(SecretTokens.Generate(ApiTokenDefaults.Prefix));
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/hosts", ct)).StatusCode);
    }

    [Fact]
    public async Task Agent_keys_are_not_api_tokens()
    {
        var owner = await app.CreateOwnerAsync("abe@example.com");
        var (_, agent) = await app.RegisterHostAsync(owner, "machine-abe");

        var response = await agent.GetAsync("/api/hosts", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_disabled_owners_tokens_stop_working()
    {
        var owner = await app.CreateOwnerAsync("bea@example.com");
        var token = TokenClient((await CreateTokenAsync(owner)).Token);

        await app.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ArgusUser>>();
            var user = (await users.FindByEmailAsync("bea@example.com"))!;
            user.DisabledAt = DateTimeOffset.UtcNow;
            return await users.UpdateAsync(user);
        });

        // The token has not been used yet, so nothing is cached and the disabled owner is seen at once.
        var response = await token.GetAsync("/api/hosts", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
