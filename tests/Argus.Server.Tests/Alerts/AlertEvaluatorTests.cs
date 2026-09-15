using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Server.Tests.Alerts;

public sealed class AlertEvaluatorTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetricSample Cpu(DateTimeOffset time, double percent) =>
        MetricsIngestionTests.FullSample(time) with { Cpu = new CpuMetrics { UsagePercent = percent } };

    private async Task<(Guid OwnerId, HttpClient Owner)> OwnerAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        return ((await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id, owner);
    }

    private DateTimeOffset Now => app.Time.GetUtcNow();

    [Fact]
    public async Task Alerts_fire_while_the_condition_holds_and_resolve_when_it_clears()
    {
        var (ownerId, owner) = await OwnerAsync("alerts-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "alerts-a-1", "web-1");
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80);

        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-20), 95), Cpu(Now.AddSeconds(-5), 97));
        var result = await app.EvaluateAsync();

        Assert.True(result.Ran);
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal("CPU usage on web-1 above 80%", alert.Title);
        Assert.Equal(96, alert.Value!.Value, precision: 3);

        // Still firing: the open alert is updated, never duplicated.
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-1), 99));
        await app.EvaluateAsync();
        alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal(97, alert.Value!.Value, precision: 3);

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-20), 10), Cpu(Now.AddSeconds(-5), 12));
        await app.EvaluateAsync();

        alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.Equal(Now, alert.ResolvedAt);
    }

    [Fact]
    public async Task Sustained_rules_wait_until_the_whole_duration_breaches()
    {
        var (ownerId, owner) = await OwnerAsync("alerts-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "alerts-b-1");
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80, durationSeconds: 120);

        await agent.SendSamplesAsync([.. Enumerable.Range(0, 5).Select(i => Cpu(Now.AddSeconds(-15 * i), 95))]);
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        await agent.SendSamplesAsync([.. Enumerable.Range(5, 4).Select(i => Cpu(Now.AddSeconds(-15 * i), 95))]);
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Firing, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);
    }

    [Fact]
    public async Task Disk_rules_alert_per_filesystem()
    {
        var (ownerId, owner) = await OwnerAsync("alerts-c@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "alerts-c-1", "files-1");
        await app.AddRuleAsync(ownerId, AlertMetric.DiskUsage, threshold: 80);

        var sample = MetricsIngestionTests.FullSample(Now.AddSeconds(-5)) with
        {
            Filesystems =
            [
                new FilesystemMetrics { MountPoint = "/", TotalBytes = 100, UsedBytes = 90, AvailableBytes = 5 },
                new FilesystemMetrics { MountPoint = "/var", TotalBytes = 50, UsedBytes = 10, AvailableBytes = 38 },
            ],
        };
        await agent.SendSamplesAsync(sample);
        await app.EvaluateAsync();

        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal("/", alert.ResourceKey);
        Assert.Equal("Disk usage on files-1 / above 80%", alert.Title);
    }

    [Fact]
    public async Task Hosts_that_stop_reporting_raise_an_offline_alert_until_they_return()
    {
        var (ownerId, owner) = await OwnerAsync("alerts-d@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "alerts-d-1", "quiet-1");
        await app.AddRuleAsync(ownerId, AlertMetric.HostOffline, threshold: 0, durationSeconds: 300);

        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(6));
        await app.EvaluateAsync();
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal("quiet-1 is offline", alert.Title);
        Assert.Equal(AlertStatus.Firing, alert.Status);

        await agent.SendSamplesAsync(Cpu(Now, 5));
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Resolved, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);
    }

    [Fact]
    public async Task Rules_cover_only_their_owners_hosts_in_scope()
    {
        var (ownerId, owner) = await OwnerAsync("alerts-e@example.com");
        var (prodId, prod) = await app.RegisterHostAsync(owner, "alerts-e-1", "prod-1");
        var (devId, dev) = await app.RegisterHostAsync(owner, "alerts-e-2", "dev-1");
        var (_, stranger) = await OwnerAsync("alerts-f@example.com");
        var (strangerHostId, strangerAgent) = await app.RegisterHostAsync(stranger, "alerts-f-1");
        (await owner.PatchAsJsonAsync($"/api/hosts/{prodId}", new { tags = new[] { "prod" } }, Ct)).EnsureSuccessStatusCode();
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80, configure: rule => rule.Tag = "prod");

        foreach (var agent in new[] { prod, dev, strangerAgent })
        {
            await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-5), 99));
        }

        await app.EvaluateAsync();

        Assert.Single(await app.AlertsForHostAsync(prodId));
        Assert.Empty(await app.AlertsForHostAsync(devId));
        Assert.Empty(await app.AlertsForHostAsync(strangerHostId));
    }

    [Fact]
    public async Task Disabling_a_rule_resolves_its_alerts()
    {
        var (ownerId, owner) = await OwnerAsync("alerts-g@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "alerts-g-1");
        var rule = await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80);
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-5), 99));
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Firing, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);

        await app.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ArgusDbContext>();
            (await db.AlertRules.SingleAsync(r => r.Id == rule.Id, Ct)).Enabled = false;
            return await db.SaveChangesAsync(Ct);
        });
        await app.EvaluateAsync();

        Assert.Equal(AlertStatus.Resolved, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);
    }
}
