using System.Net;
using System.Net.Http.Json;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Auth;

public sealed class LoginTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Login_me_and_logout_round_trip()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("alice@example.com", Password);
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", ct)).StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "alice@example.com", password = Password }, ct);
        login.EnsureSuccessStatusCode();
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("argus_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me", ct);
        Assert.Equal("alice@example.com", me!.Email);
        Assert.False(me.IsAdmin);

        (await client.PostAsync("/api/auth/logout", null, ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me", ct)).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("bob@example.com", Password);
        var client = app.CreateClient();

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "bob@example.com", password = "not the password" }, ct);
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "not the password" }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var wrongProblem = await wrong.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        var unknownProblem = await unknown.Content.ReadFromJsonAsync<ProblemDetails>(ct);
        Assert.Equal(wrongProblem!.Title, unknownProblem!.Title);
        Assert.Equal(wrongProblem.Detail, unknownProblem.Detail);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("carol@example.com", Password);
        var client = app.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new { email = "carol@example.com", password = "guess " + attempt }, ct);
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "carol@example.com", password = Password }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Account locked", (await response.Content.ReadFromJsonAsync<ProblemDetails>(ct))!.Title);
    }

    [Fact]
    public async Task Disabled_accounts_cannot_sign_in()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = await app.CreateUserAsync("dave@example.com", Password);
        await app.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ArgusUser>>();
            var stored = await users.FindByIdAsync(user.Id.ToString());
            stored!.DisabledAt = DateTimeOffset.UtcNow;
            return await users.UpdateAsync(stored);
        });

        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "dave@example.com", password = Password }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Account disabled", (await response.Content.ReadFromJsonAsync<ProblemDetails>(ct))!.Title);
    }
}
