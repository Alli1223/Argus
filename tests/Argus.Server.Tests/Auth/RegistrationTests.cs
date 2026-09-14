using System.Net;
using System.Net.Http.Json;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Argus.Server.Tests.Auth;

public sealed class RegistrationTests(OpenRegistrationFixture app) : IClassFixture<OpenRegistrationFixture>
{
    internal const string Password = "correct horse battery";

    internal static object Account(string email) => new { email, password = Password, displayName = email.Split('@')[0] };

    [Fact]
    public async Task Visitors_can_register_once_setup_is_done()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = app.CreateClient();
        Assert.True((await client.GetFromJsonAsync<AuthStatusResponse>("/api/auth/status", ct))!.RegistrationEnabled);

        // Before setup, registering would take the account that is meant to become the administrator.
        var early = await client.PostAsJsonAsync("/api/auth/register", Account("early@example.com"), ct);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        await app.CreateUserAsync("admin@example.com", Password, Roles.Admin);

        var response = await client.PostAsJsonAsync("/api/auth/register", Account("erin@example.com"), ct);
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(ct);
        Assert.Equal([Roles.User], user!.Roles);
        Assert.Equal("erin@example.com", (await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", ct))!.Email);

        var duplicate = await app.CreateClient().PostAsJsonAsync("/api/auth/register", Account("erin@example.com"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("email", (await duplicate.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct))!.Errors.Keys);
    }
}

public sealed class RegistrationDisabledTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    [Fact]
    public async Task Registration_is_refused_unless_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("admin@example.com", RegistrationTests.Password, Roles.Admin);

        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/register", RegistrationTests.Account("frank@example.com"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
