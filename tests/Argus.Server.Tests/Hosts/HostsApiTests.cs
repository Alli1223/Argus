using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Dashboard;
using Argus.Server.Features.Hosts;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Argus.Server.Tests.Hosts;

public sealed class HostsApiTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owners_see_their_hosts_with_status_and_latest_metrics()
    {
        var owner = await app.CreateOwnerAsync("hosts-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "hosts-a-1", "db-1");
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow));

        var hosts = await owner.GetJsonAsync<List<HostSummary>>("/api/hosts");

        var host = Assert.Single(hosts!);
        Assert.Equal(hostId, host.Id);
        Assert.Equal("db-1", host.DisplayName);
        Assert.Equal(HostStatus.Online, host.Status);
        Assert.Equal(HostPlatform.Linux, host.Platform);
        Assert.Equal(42, host.Latest!.CpuPercent, precision: 3);
        Assert.Equal(40, host.Latest.MemoryPercent, precision: 3);
        Assert.Equal(40 / 95.0 * 100, host.Latest.DiskUsedPercent!.Value, precision: 3);
    }

    [Fact]
    public async Task Hosts_are_private_to_their_owner_but_visible_to_admins()
    {
        var owner = await app.CreateOwnerAsync("hosts-b@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "hosts-b-1");
        var stranger = await app.CreateOwnerAsync("hosts-c@example.com");
        var admin = await app.CreateOwnerAsync("hosts-admin@example.com", Roles.Admin);

        Assert.DoesNotContain((await stranger.GetJsonAsync<List<HostSummary>>("/api/hosts"))!, host => host.Id == hostId);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}/metrics", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}/processes", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/hosts/{hostId}", Ct)).StatusCode);

        Assert.Contains((await admin.GetJsonAsync<List<HostSummary>>("/api/hosts"))!, host => host.Id == hostId);
        (await admin.GetAsync($"/api/hosts/{hostId}", Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Host_detail_includes_the_inventory()
    {
        var owner = await app.CreateOwnerAsync("hosts-d@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "hosts-d-1", "app-7");

        var detail = await owner.GetJsonAsync<HostDetail>($"/api/hosts/{hostId}");

        Assert.Equal("app-7", detail!.Hostname);
        Assert.Equal("Ubuntu 24.04 LTS", detail.OsName);
        Assert.Equal(4, detail.CpuLogicalProcessors);
        Assert.Equal(["10.0.0.5"], detail.IpAddresses);
        Assert.Equal(HostStatus.Online, detail.Status);
        Assert.Null(detail.Latest);
    }

    [Fact]
    public async Task Owners_rename_tag_and_annotate_hosts()
    {
        var owner = await app.CreateOwnerAsync("hosts-e@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "hosts-e-1");

        var response = await owner.PatchAsJsonAsync($"/api/hosts/{hostId}",
            new { displayName = " Primary DB ", tags = new[] { "DB", "prod" }, notes = "Rack 4" }, Ct);

        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<HostDetail>(TestJson.Options, Ct);
        Assert.Equal("Primary DB", detail!.DisplayName);
        Assert.Equal(["db", "prod"], detail.Tags);
        Assert.Equal("Rack 4", detail.Notes);

        var renamedOnly = await owner.PatchAsJsonAsync($"/api/hosts/{hostId}", new { displayName = "DB" }, Ct);
        Assert.Equal(["db", "prod"], (await renamedOnly.Content.ReadFromJsonAsync<HostDetail>(TestJson.Options, Ct))!.Tags);

        var badTags = await owner.PatchAsJsonAsync($"/api/hosts/{hostId}", new { tags = new[] { "two words" } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badTags.StatusCode);
        Assert.Contains("tags", (await badTags.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors.Keys);

        var emptyName = await owner.PatchAsJsonAsync($"/api/hosts/{hostId}", new { displayName = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, emptyName.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_host_removes_its_data_and_revokes_its_agent()
    {
        var owner = await app.CreateOwnerAsync("hosts-f@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "hosts-f-1");
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/hosts/{hostId}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/hosts/{hostId}", Ct)).StatusCode);
        var rejected = await agent.PostAsJsonAsync(AgentApi.Metrics,
            new MetricsBatch { Samples = [MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow)] }, AgentJsonContext.Default.MetricsBatch, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        var remaining = await app.WithScopeAsync(async services =>
        {
            await using var command = services.GetRequiredService<NpgsqlDataSource>().CreateCommand(
                "SELECT (SELECT count(*) FROM host_metrics WHERE host_id = @id) + (SELECT count(*) FROM filesystem_metrics WHERE host_id = @id)");
            command.Parameters.AddWithValue("id", hostId);
            return (long)(await command.ExecuteScalarAsync(Ct))!;
        });
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task The_dashboard_summarizes_the_fleet()
    {
        var owner = await app.CreateOwnerAsync("hosts-g@example.com");
        var (busyId, busy) = await app.RegisterHostAsync(owner, "hosts-g-1", "busy");
        var (quietId, _) = await app.RegisterHostAsync(owner, "hosts-g-2", "quiet");
        await busy.SendSamplesAsync(MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow));
        await app.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ArgusDbContext>();
            var quiet = await db.Hosts.SingleAsync(h => h.Id == quietId, Ct);
            quiet.LastSeenAt = DateTimeOffset.UtcNow.AddHours(-1);
            return await db.SaveChangesAsync(Ct);
        });

        var summary = await owner.GetJsonAsync<DashboardSummary>("/api/dashboard/summary");

        Assert.Equal(2, summary!.TotalHosts);
        Assert.Equal(1, summary.OnlineHosts);
        Assert.Equal(1, summary.OfflineHosts);
        Assert.Equal(2, summary.Platforms["Linux"]);
        Assert.Equal(busyId, Assert.Single(summary.BusiestByCpu).Id);
        Assert.Equal(busyId, Assert.Single(summary.FullestDisks).Id);
    }
}
