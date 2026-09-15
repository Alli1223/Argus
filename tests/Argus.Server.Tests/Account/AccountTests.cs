using System.Net;
using System.Net.Http.Json;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Argus.Server.Tests.Account;

public sealed class AccountTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private const string Password = "correct horse battery";
    private const string NewPassword = "a brand new passphrase";

    [Fact]
    public async Task Users_can_change_their_password()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("gina@example.com", Password);
        var client = await app.CreateSignedInClientAsync("gina@example.com", Password);

        var wrongCurrent = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "not it at all", newPassword = NewPassword }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Contains("currentPassword", (await wrongCurrent.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct))!.Errors.Keys);

        var weak = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = Password, newPassword = "short" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("newPassword", (await weak.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct))!.Errors.Keys);

        var changed = await client.PostAsJsonAsync("/api/account/password", new { currentPassword = Password, newPassword = NewPassword }, ct);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // The session that made the change stays signed in, and only the new password works from now on.
        (await client.GetAsync("/api/auth/me", ct)).EnsureSuccessStatusCode();
        var oldPassword = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "gina@example.com", password = Password }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        await app.CreateSignedInClientAsync("gina@example.com", NewPassword);
    }

    [Fact]
    public async Task Users_can_rename_themselves()
    {
        var ct = TestContext.Current.CancellationToken;
        await app.CreateUserAsync("hank@example.com", Password);
        var client = await app.CreateSignedInClientAsync("hank@example.com", Password);

        var response = await client.PutAsJsonAsync("/api/account/profile", new { displayName = "  Hank Hill " }, ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal("Hank Hill", (await response.Content.ReadFromJsonAsync<CurrentUserResponse>(ct))!.DisplayName);
    }

    [Fact]
    public async Task Account_endpoints_require_a_session()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await app.CreateClient().PutAsJsonAsync("/api/account/profile", new { displayName = "Nobody" }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
