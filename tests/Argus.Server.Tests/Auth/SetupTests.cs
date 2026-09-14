using System.Net;
using System.Net.Http.Json;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Argus.Server.Tests.Auth;

public sealed class SetupTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Fact]
    public async Task First_run_setup_creates_an_admin_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = app.CreateClient();

        var status = await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status", ct);
        Assert.True(status!.SetupRequired);
        Assert.False(status.RegistrationEnabled);

        // Invalid requests are rejected without using up the one-time setup.
        var invalid = await client.PostAsJsonAsync("/api/auth/setup", new { email = "not-an-email", password = "", displayName = "" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var validation = await invalid.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct);
        Assert.Contains(validation!.Errors.Keys, key => key.Equals("email", StringComparison.OrdinalIgnoreCase));

        var weak = await client.PostAsJsonAsync("/api/auth/setup", new { email = "admin@example.com", password = "short", displayName = "Admin" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("password", (await weak.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct))!.Errors.Keys);

        var created = await client.PostAsJsonAsync("/api/auth/setup",
            new { email = "admin@example.com", password = "correct horse battery", displayName = "Admin" }, ct);
        created.EnsureSuccessStatusCode();
        var admin = await created.Content.ReadFromJsonAsync<CurrentUserResponse>(ct);
        Assert.True(admin!.IsAdmin);
        Assert.Equal([Roles.Admin], admin.Roles);

        // The setup response also signed the new administrator in.
        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", ct);
        Assert.Equal("admin@example.com", me!.Email);

        var second = await app.CreateClient().PostAsJsonAsync("/api/auth/setup",
            new { email = "second@example.com", password = "correct horse battery", displayName = "Second" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.False((await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status", ct))!.SetupRequired);
    }
}
