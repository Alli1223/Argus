using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Updates;
using Argus.Server.Infrastructure;
using Argus.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Updates;

/// <summary>Asking the updater service to install a release, through the directory the two share.</summary>
public sealed class ServerSelfUpdateTests(UpdatesFixture app) : IClassFixture<UpdatesFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FileIn(string name) => Path.Combine(app.ServerUpdatesDirectory, name);

    /// <summary>Starts each test with a newer release out and a quiet directory.</summary>
    private async Task<HttpClient> AdminAsync(string email)
    {
        foreach (var file in Directory.GetFiles(app.ServerUpdatesDirectory))
        {
            File.Delete(file);
        }

        app.GitHub.Latest = FakeGitHub.FakeRelease.For("9.9.3");
        await app.CheckAsync();
        return await app.CreateOwnerAsync(email, Roles.Admin);
    }

    private void Heartbeat(TimeSpan age, string? problem = null) =>
        File.WriteAllText(FileIn("updater.json"), JsonSerializer.Serialize(
            new { updaterVersion = "9.9.3", heartbeatAt = DateTimeOffset.UtcNow - age, problem }, JsonSerializerOptions.Web));

    private void Status(string id, string state) =>
        File.WriteAllText(FileIn("status.json"), JsonSerializer.Serialize(new
        {
            id,
            from = ServerVersion.Current,
            to = "9.9.3",
            requestedBy = "admin@example.com",
            state,
            startedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            finishedAt = (DateTimeOffset?)null,
            error = state == "rolled-back" ? "Argus 9.9.3 did not start properly." : null,
            backup = "/srv/argus/deploy/backups/argus.dump",
            serverLog = (string?)null,
            log = new[] { new { at = DateTimeOffset.UtcNow, message = "Backing up the database." } },
        }, JsonSerializerOptions.Web));

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, string version) =>
        client.PostAsJsonAsync("/api/updates/server", new { version }, Ct);

    private static async Task<string?> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail;
    }

    [Fact]
    public async Task Updates_wait_for_a_running_updater()
    {
        var admin = await AdminAsync("self-update-a@example.com");

        var state = await admin.GetJsonAsync<ServerSelfUpdate>("/api/updates/server");
        Assert.False(state!.Available);
        Assert.Equal("The updater is not running.", state.Unavailable);
        Assert.Equal("The updater is not running.", await ProblemAsync(await RequestAsync(admin, "9.9.3")));

        Heartbeat(TimeSpan.FromMinutes(10));
        Assert.StartsWith("The updater has not been heard from", (await admin.GetJsonAsync<ServerSelfUpdate>("/api/updates/server"))!.Unavailable);

        Heartbeat(TimeSpan.Zero, problem: "Mount the Compose project directory (/srv/argus) into the updater at /deploy.");
        Assert.Equal(
            "Mount the Compose project directory (/srv/argus) into the updater at /deploy.",
            (await admin.GetJsonAsync<ServerSelfUpdate>("/api/updates/server"))!.Unavailable);
        Assert.False(File.Exists(FileIn("request.json")));
    }

    [Fact]
    public async Task Administrators_ask_the_updater_for_the_latest_release_once()
    {
        var admin = await AdminAsync("self-update-b@example.com");
        Heartbeat(TimeSpan.FromSeconds(3));

        var accepted = await RequestAsync(admin, "9.9.3");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var state = (await accepted.Content.ReadFromJsonAsync<ServerSelfUpdate>(TestJson.Options, Ct))!;
        Assert.True(state.Available);
        Assert.Equal("9.9.3", state.PendingVersion);

        using var request = JsonDocument.Parse(await File.ReadAllTextAsync(FileIn("request.json"), Ct));
        Assert.Equal("9.9.3", request.RootElement.GetProperty("version").GetString());
        Assert.Equal("self-update-b@example.com", request.RootElement.GetProperty("requestedBy").GetString());
        Assert.Equal("An update to 9.9.3 is already waiting to start.", await ProblemAsync(await RequestAsync(admin, "9.9.3")));
    }

    [Fact]
    public async Task Only_the_latest_newer_release_can_be_installed()
    {
        var admin = await AdminAsync("self-update-c@example.com");
        Heartbeat(TimeSpan.Zero);

        Assert.Equal("Only the latest release, 9.9.3, can be installed.", await ProblemAsync(await RequestAsync(admin, "9.9.2")));

        app.GitHub.Latest = FakeGitHub.FakeRelease.For(ServerVersion.Current);
        await app.CheckAsync();
        Assert.Equal($"This server already runs {ServerVersion.Current}.", await ProblemAsync(await RequestAsync(admin, ServerVersion.Current)));
        Assert.False(File.Exists(FileIn("request.json")));
    }

    [Fact]
    public async Task The_latest_update_shows_its_progress_and_blocks_another_until_it_ends()
    {
        var admin = await AdminAsync("self-update-d@example.com");
        File.WriteAllText(FileIn("request.json"), """{"id":"run-1","version":"9.9.3","requestedBy":"x","requestedAt":"2026-09-16T12:00:00Z"}""");
        Status("run-1", "backing-up");
        // Mid-update the updater may stay quiet for a while without counting as gone.
        Heartbeat(TimeSpan.FromMinutes(10));

        var state = (await admin.GetJsonAsync<ServerSelfUpdate>("/api/updates/server"))!;
        Assert.True(state.Available);
        Assert.Null(state.PendingVersion);
        Assert.Equal(("run-1", "backing-up", true), (state.LastRun!.Id, state.LastRun.State, state.LastRun.InProgress));
        Assert.Equal("Backing up the database.", Assert.Single(state.LastRun.Log!).Message);
        Assert.Equal("An update to 9.9.3 is already running.", await ProblemAsync(await RequestAsync(admin, "9.9.3")));

        Status("run-1", "rolled-back");
        Heartbeat(TimeSpan.Zero);
        state = (await admin.GetJsonAsync<ServerSelfUpdate>("/api/updates/server"))!;
        Assert.Equal("Argus 9.9.3 did not start properly.", state.LastRun!.Error);
        Assert.Equal(HttpStatusCode.Accepted, (await RequestAsync(admin, "9.9.3")).StatusCode);
    }

    [Fact]
    public async Task Only_administrators_see_or_start_server_updates()
    {
        await AdminAsync("self-update-e@example.com");
        Heartbeat(TimeSpan.Zero);
        var user = await app.CreateOwnerAsync("self-update-f@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/updates/server", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RequestAsync(user, "9.9.3")).StatusCode);
        Assert.False(File.Exists(FileIn("request.json")));
    }
}
