using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Enrollment;
using Argus.Server.Infrastructure;
using Argus.Server.Tests.Infrastructure;

namespace Argus.Server.Tests.Enrollment;

public sealed class EnrollmentTokenTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    internal const string Password = "correct horse battery";

    internal static async Task<CreatedEnrollmentToken> CreateTokenAsync(HttpClient client, object? request = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/enrollment-tokens", request ?? new { name = "Servers" }, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedEnrollmentToken>(ct))!;
    }

    private async Task<HttpClient> SignedInAsync(string email)
    {
        await app.CreateUserAsync(email, Password);
        return await app.CreateSignedInClientAsync(email, Password);
    }

    [Fact]
    public async Task Tokens_are_shown_once_and_listed_without_the_secret()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await SignedInAsync("olivia@example.com");

        var created = await CreateTokenAsync(client, new { name = "Web servers", maxUses = 5, expiresInHours = 24, tags = new[] { " Web ", "prod", "web" } });

        Assert.True(SecretTokens.LooksValid(created.Token, AgentApi.EnrollmentTokenPrefix));
        Assert.True(created.Summary.IsActive);
        Assert.Equal(["web", "prod"], created.Summary.Tags);
        Assert.NotNull(created.Summary.ExpiresAt);

        var json = await client.GetStringAsync("/api/enrollment-tokens", ct);
        Assert.DoesNotContain(created.Token, json, StringComparison.Ordinal);
        var summary = Assert.Single(JsonSerializer.Deserialize<List<EnrollmentTokenSummary>>(json, JsonSerializerOptions.Web)!);
        Assert.Equal(created.Summary.Id, summary.Id);
        Assert.StartsWith(summary.TokenPrefix.TrimEnd('…'), created.Token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owners_revoke_their_tokens_and_nobody_else_can()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await SignedInAsync("pat@example.com");
        var other = await SignedInAsync("quinn@example.com");
        var created = await CreateTokenAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/enrollment-tokens/{created.Summary.Id}", ct)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<EnrollmentTokenSummary>>("/api/enrollment-tokens", ct))!);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/enrollment-tokens/{created.Summary.Id}", ct)).StatusCode);
        var summary = Assert.Single((await owner.GetFromJsonAsync<List<EnrollmentTokenSummary>>("/api/enrollment-tokens", ct))!);
        Assert.False(summary.IsActive);
        Assert.NotNull(summary.RevokedAt);
    }

    [Fact]
    public async Task Invalid_requests_are_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await SignedInAsync("rae@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/enrollment-tokens", new { name = "" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/enrollment-tokens", new { name = "x", maxUses = 0 }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/enrollment-tokens", new { name = "x", tags = new[] { "has space" } }, ct)).StatusCode);
    }

    [Fact]
    public async Task Tokens_require_a_session()
    {
        var response = await app.CreateClient().GetAsync("/api/enrollment-tokens", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
