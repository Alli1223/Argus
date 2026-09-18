using System.Net;
using System.Net.Http.Json;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Notifications;
using Argus.Server.Features.Settings;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Settings;

/// <summary>The mail server administrators set in the web app, which replaces the one in the Compose file.</summary>
public sealed class EmailSettingsApiTests(NotificationsFixture app) : IClassFixture<NotificationsFixture>
{
    private const string Route = "/api/settings/email";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Settings(string host = "smtp.example.com", string? password = null, string from = "argus@example.com") =>
        new { host, port = 2525, security = "StartTls", username = "argus", password, from, fromName = "Argus test" };

    private Task<HttpClient> AdminAsync(string email) => app.CreateOwnerAsync(email, Roles.Admin);

    private static async Task<EmailSettingsResponse> GetAsync(HttpClient admin) =>
        (await admin.GetJsonAsync<EmailSettingsResponse>(Route))!;

    private async Task<EmailSettingsResponse> SaveAsync(HttpClient admin, object request)
    {
        var response = await admin.PutAsJsonAsync(Route, request, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EmailSettingsResponse>(TestJson.Options, Ct))!;
    }

    /// <summary>The password never comes back over the API, so read it from the store itself.</summary>
    private Task<string?> StoredPasswordAsync() =>
        app.WithScopeAsync(async services =>
            (await services.GetRequiredService<EmailSettingsStore>().CurrentAsync(Ct)).Password);

    [Fact]
    public async Task Saved_settings_replace_the_ones_in_the_file()
    {
        var admin = await AdminAsync("email-settings-a@example.com");

        var fromFile = await GetAsync(admin);
        Assert.Equal(EmailSettingsSource.File, fromFile.Source);
        Assert.Equal("smtp.argus.test", fromFile.Host);
        Assert.True(fromFile.Configured);

        var saved = await SaveAsync(admin, Settings(password: "app-password"));
        Assert.Equal(EmailSettingsSource.App, saved.Source);
        Assert.Equal(("smtp.example.com", 2525, SmtpSecurity.StartTls, "argus"), (saved.Host, saved.Port, saved.Security, saved.Username));
        Assert.Equal(("argus@example.com", "Argus test"), (saved.From, saved.FromName));
        Assert.True(saved.HasPassword);
        Assert.Equal("email-settings-a@example.com", saved.UpdatedBy);
        Assert.Equal("app-password", await StoredPasswordAsync());

        // Saving without a password keeps the one already saved; an empty password clears it.
        var again = await SaveAsync(admin, Settings(host: "smtp.other.test"));
        Assert.True(again.HasPassword);
        Assert.Equal("app-password", await StoredPasswordAsync());

        var cleared = await SaveAsync(admin, Settings(password: ""));
        Assert.False(cleared.HasPassword);
        Assert.Null(await StoredPasswordAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Route, Ct)).StatusCode);
        var backToFile = await GetAsync(admin);
        Assert.Equal((EmailSettingsSource.File, "smtp.argus.test"), (backToFile.Source, backToFile.Host));
    }

    [Fact]
    public async Task Settings_that_cannot_work_are_refused()
    {
        var admin = await AdminAsync("email-settings-b@example.com");

        var response = await admin.PutAsJsonAsync(Route, new { host = "smtp.example.com/submit", port = 0, from = "not-an-address" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestJson.Options, Ct))!;
        Assert.Equal(["from", "host", "port"], problem.Errors.Keys.Order());
    }

    [Fact]
    public async Task A_test_email_says_what_the_mail_server_answered()
    {
        var admin = await AdminAsync("email-settings-c@example.com");
        await SaveAsync(admin, Settings(from: "alerts@example.com"));

        var sent = await admin.PostAsJsonAsync($"{Route}/test", new { to = " ops@example.com " }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);
        var message = Assert.Single(app.Email.SentTo("ops@example.com"));
        Assert.Equal("Test from Argus", message.Subject);
        Assert.Equal("\"Argus test\" <alerts@example.com>", message.From.ToString());

        var badAddress = await admin.PostAsJsonAsync($"{Route}/test", new { to = "nobody" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badAddress.StatusCode);

        app.Email.FailWith = "the mailbox is full";
        try
        {
            var failed = await admin.PostAsJsonAsync($"{Route}/test", new { to = "ops@example.com" }, Ct);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            var problem = (await failed.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options, Ct))!;
            Assert.Equal("The test was not sent", problem.Title);
            Assert.Contains("the mailbox is full", problem.Detail);
        }
        finally
        {
            app.Email.FailWith = null;
            await admin.DeleteAsync(Route, Ct);
        }
    }

    [Fact]
    public async Task Only_administrators_see_the_mail_server()
    {
        var user = await app.CreateOwnerAsync("email-settings-d@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(Route, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync(Route, Settings(), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync(Route, Ct)).StatusCode);
    }
}
