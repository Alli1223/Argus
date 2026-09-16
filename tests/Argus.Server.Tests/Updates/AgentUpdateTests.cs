using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Argus.Contracts.Agent;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Updates;
using Argus.Server.Tests.Agents;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Updates;

public sealed class AgentUpdateTests(UpdatesFixture app) : IClassFixture<UpdatesFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<MetricsBatchResponse> ReportAsync(HttpClient agent)
    {
        var response = await agent.PostAsJsonAsync(
            AgentApi.Metrics,
            new MetricsBatch { Samples = [MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow.AddSeconds(-5))] },
            AgentJsonContext.Default.MetricsBatch,
            Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(AgentJsonContext.Default.MetricsBatchResponse, Ct))!;
    }

    private static async Task<HostAgentUpdate?> AgentUpdateOfAsync(HttpClient owner, Guid hostId) =>
        (await owner.GetJsonAsync<HostDetail>($"/api/hosts/{hostId}"))!.AgentUpdate;

    [Fact]
    public async Task Agents_asked_to_update_get_a_checked_build_until_they_run_it()
    {
        var release = FakeGitHub.FakeRelease.For("9.9.1");
        app.GitHub.Latest = release;
        var owner = await app.CreateOwnerAsync("updates-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "updates-a-1");
        await app.CheckAsync();

        Assert.Equal(new HostAgentUpdate("9.9.1", null, null, null), await AgentUpdateOfAsync(owner, hostId));
        Assert.Null((await ReportAsync(agent)).Update);

        var requested = await owner.PostAsync($"/api/hosts/{hostId}/agent-update", null, Ct);
        requested.EnsureSuccessStatusCode();
        Assert.Equal("9.9.1", (await requested.Content.ReadFromJsonAsync<HostDetail>(TestJson.Options, Ct))!.AgentUpdate!.Requested);

        await app.PrepareAsync("linux-x64");
        var build = release.Files["argus-agent-linux-x64"];
        var offer = (await ReportAsync(agent)).Update!;
        Assert.Equal(("9.9.1", Convert.ToHexStringLower(SHA256.HashData(build)), (long)build.Length), (offer.Version, offer.Sha256, offer.Size));
        Assert.Equal(offer, await agent.GetFromJsonAsync(AgentApi.UpdateOffer, AgentJsonContext.Default.AgentUpdateOffer, Ct));
        Assert.Equal(build, await agent.GetByteArrayAsync(AgentApi.UpdateDownload, Ct));

        // The updated agent reports its new version, which ends the update.
        (await agent.PutAsJsonAsync(AgentApi.Inventory,
            new InventoryReport { AgentVersion = "9.9.1", SystemInfo = AgentRegistrationTests.Registration("", "").SystemInfo },
            AgentJsonContext.Default.InventoryReport, Ct)).EnsureSuccessStatusCode();

        Assert.Null(await AgentUpdateOfAsync(owner, hostId));
        Assert.Null((await ReportAsync(agent)).Update);
        Assert.Equal(HttpStatusCode.NoContent, (await agent.GetAsync(AgentApi.UpdateOffer, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(AgentApi.UpdateDownload, Ct)).StatusCode);
    }

    [Fact]
    public async Task Builds_that_do_not_match_their_checksums_are_never_offered()
    {
        app.GitHub.Latest = FakeGitHub.FakeRelease.For("9.9.2") with
        {
            Checksums = $"{new string('0', 64)}  argus-agent-linux-x64\n",
        };
        var owner = await app.CreateOwnerAsync("updates-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "updates-b-1");
        await app.CheckAsync();

        (await owner.PostAsync($"/api/hosts/{hostId}/agent-update", null, Ct)).EnsureSuccessStatusCode();
        await Assert.ThrowsAsync<InvalidDataException>(() => app.PrepareAsync("linux-x64"));

        Assert.Null((await ReportAsync(agent)).Update);
        var state = (await AgentUpdateOfAsync(owner, hostId))!;
        Assert.Null(state.Requested);
        Assert.Contains("does not match SHA256SUMS", state.Error);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(AgentApi.UpdateDownload, Ct)).StatusCode);
    }

    [Fact]
    public async Task Failed_updates_are_reported_and_can_be_cleared()
    {
        app.GitHub.Latest = FakeGitHub.FakeRelease.For("9.9.3");
        var owner = await app.CreateOwnerAsync("updates-c@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "updates-c-1");
        await app.CheckAsync();
        (await owner.PostAsync($"/api/hosts/{hostId}/agent-update", null, Ct)).EnsureSuccessStatusCode();

        (await agent.PostAsJsonAsync(AgentApi.UpdateResult,
            new AgentUpdateResult { Version = "9.9.3", Succeeded = false, Error = "The new agent did not start." },
            AgentJsonContext.Default.AgentUpdateResult, Ct)).EnsureSuccessStatusCode();

        Assert.Equal(new HostAgentUpdate("9.9.3", null, null, "The new agent did not start."), await AgentUpdateOfAsync(owner, hostId));

        (await owner.DeleteAsync($"/api/hosts/{hostId}/agent-update", Ct)).EnsureSuccessStatusCode();
        Assert.Equal(new HostAgentUpdate("9.9.3", null, null, null), await AgentUpdateOfAsync(owner, hostId));
    }

    [Fact]
    public async Task Every_outdated_agent_can_be_asked_at_once()
    {
        app.GitHub.Latest = FakeGitHub.FakeRelease.For("9.9.4");
        var owner = await app.CreateOwnerAsync("updates-d@example.com");
        var (firstId, _) = await app.RegisterHostAsync(owner, "updates-d-1");
        var (secondId, _) = await app.RegisterHostAsync(owner, "updates-d-2", "web-2");
        var stranger = await app.CreateOwnerAsync("updates-e@example.com");
        var (strangerHostId, _) = await app.RegisterHostAsync(stranger, "updates-e-1");
        await app.CheckAsync();

        var response = await owner.PostAsync("/api/hosts/agent-updates", null, Ct);
        Assert.Equal(new AgentUpdateRequests(2), await response.Content.ReadFromJsonAsync<AgentUpdateRequests>(Ct));
        Assert.Equal(new AgentUpdateRequests(0), await (await owner.PostAsync("/api/hosts/agent-updates", null, Ct))
            .Content.ReadFromJsonAsync<AgentUpdateRequests>(Ct));

        Assert.Equal("9.9.4", (await AgentUpdateOfAsync(owner, firstId))!.Requested);
        Assert.Equal("9.9.4", (await AgentUpdateOfAsync(owner, secondId))!.Requested);
        Assert.Null((await AgentUpdateOfAsync(stranger, strangerHostId))!.Requested);
    }

    [Fact]
    public async Task Hosts_already_on_the_latest_release_have_nothing_to_update()
    {
        app.GitHub.Latest = FakeGitHub.FakeRelease.For("1.0.0");
        var owner = await app.CreateOwnerAsync("updates-f@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "updates-f-1");
        await app.CheckAsync();

        Assert.Null(await AgentUpdateOfAsync(owner, hostId));
        var refused = await owner.PostAsync($"/api/hosts/{hostId}/agent-update", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("The agent already runs 1.0.0, and the latest release is 1.0.0.",
            (await refused.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Detail);
    }

    [Fact]
    public async Task Administrators_see_whether_the_server_is_up_to_date()
    {
        app.GitHub.Latest = FakeGitHub.FakeRelease.For("9.9.5");
        var admin = await app.CreateOwnerAsync("updates-admin@example.com", Roles.Admin);
        var user = await app.CreateOwnerAsync("updates-g@example.com");

        var info = (await (await admin.PostAsync("/api/updates/check", null, Ct)).Content.ReadFromJsonAsync<ServerUpdateInfo>(TestJson.Options, Ct))!;
        Assert.True(info.Enabled && info.UpdateAvailable);
        Assert.Equal(("9.9.5", "v9.9.5", "Argus 9.9.5"), (info.Latest!.Version, info.Latest.Tag, info.Latest.Name));
        Assert.Null(info.Error);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/updates", Ct)).StatusCode);

        // A failed check says why, and keeps what the last one found.
        app.GitHub.Broken = true;
        try
        {
            await admin.PostAsync("/api/updates/check", null, Ct);
            var after = (await admin.GetJsonAsync<ServerUpdateInfo>("/api/updates"))!;
            Assert.Equal("GitHub answered 503 Service Unavailable.", after.Error);
            Assert.Equal("9.9.5", after.Latest!.Version);
        }
        finally
        {
            app.GitHub.Broken = false;
        }
    }
}
