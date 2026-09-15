using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;

namespace Argus.Server.Tests.Alerts;

public sealed class ServiceAlertTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private DateTimeOffset Now => app.Time.GetUtcNow();

    private static ServiceProblem Failed(string name) => new() { Name = name, State = "failed" };

    private static MetricSample Check(DateTimeOffset time, params ServiceProblem[] failures) =>
        MetricsIngestionTests.FullSample(time) with { FailedServices = failures };

    private async Task<(Guid OwnerId, HttpClient Owner)> OwnerAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        return ((await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id, owner);
    }

    [Fact]
    public async Task A_failed_service_alerts_and_resolves_when_it_recovers()
    {
        var (ownerId, owner) = await OwnerAsync("service-alerts-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "service-alerts-a-1", "web-1");
        await app.AddRuleAsync(ownerId, AlertMetric.ServiceFailed, threshold: 0);

        await agent.SendSamplesAsync(Check(Now.AddSeconds(-5), Failed("nginx.service")));
        await app.EvaluateAsync();

        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal("nginx.service", alert.ResourceKey);
        Assert.Equal("nginx.service failed on web-1", alert.Title);

        // Still failing: the same alert stays open.
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Check(Now.AddSeconds(-5), Failed("nginx.service")));
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Firing, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Check(Now.AddSeconds(-5)));
        await app.EvaluateAsync();

        alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.Equal(Now, alert.ResolvedAt);
    }

    [Fact]
    public async Task Rules_can_watch_one_service_and_wait_for_their_duration()
    {
        var (ownerId, owner) = await OwnerAsync("service-alerts-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "service-alerts-b-1");
        await app.AddRuleAsync(ownerId, AlertMetric.ServiceFailed, threshold: 0, durationSeconds: 120,
            configure: rule => rule.ResourceFilter = "backup.service");

        await agent.SendSamplesAsync(Check(Now.AddSeconds(-5), Failed("backup.service"), Failed("cron.service")));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(3));
        await agent.SendSamplesAsync(Check(Now.AddSeconds(-5), Failed("backup.service"), Failed("cron.service")));
        await app.EvaluateAsync();

        Assert.Equal("backup.service", Assert.Single(await app.AlertsForHostAsync(hostId)).ResourceKey);
    }
}
