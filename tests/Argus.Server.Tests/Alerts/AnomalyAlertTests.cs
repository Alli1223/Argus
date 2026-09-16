using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Argus.Server.Tests.Alerts;

public sealed class AnomalyAlertTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => app.Time.GetUtcNow();

    private static MetricSample Cpu(DateTimeOffset time, double percent) =>
        MetricsIngestionTests.FullSample(time) with { Cpu = new CpuMetrics { UsagePercent = percent } };

    /// <summary>A sample every 15 seconds over the last five minutes.</summary>
    private MetricSample[] LastFiveMinutes(double percent) =>
        [.. Enumerable.Range(0, 21).Select(i => Cpu(Now.AddSeconds(-15 * i), percent))];

    private async Task<(Guid OwnerId, HttpClient Owner)> OwnerAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        return ((await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id, owner);
    }

    private Task<AlertRule> AddAnomalyRuleAsync(Guid ownerId) =>
        app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 3, durationSeconds: 300,
            configure: rule => rule.Condition = AlertCondition.Anomaly);

    /// <summary>
    /// Two days of readings, one a minute, with CPU swinging gently around 10%. The rollup is refreshed
    /// by hand: the database's own refresh job may already have moved past these times.
    /// </summary>
    private Task SeedHistoryAsync(Guid hostId) =>
        app.WithScopeAsync(async services =>
        {
            var range = new { host_id = hostId, from = Now.AddDays(-2), to = Now.AddMinutes(-15) };
            await using var connection = await services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(Ct);
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO host_metrics (time, host_id, cpu_usage_pct, mem_total_bytes, mem_used_bytes, mem_available_bytes,
                                          swap_total_bytes, swap_used_bytes, process_count, uptime_seconds)
                SELECT t, @host_id, 10 + 3 * sin(extract(epoch FROM t) / 1800), 8000000000, 4000000000, 4000000000, 0, 0, 120, 86400
                FROM generate_series(@from, @to, INTERVAL '1 minute') AS t
                """, range, cancellationToken: Ct));
            return await connection.ExecuteAsync(new CommandDefinition(
                "CALL refresh_continuous_aggregate('host_metrics_5m', @from::timestamptz, @to::timestamptz)", range, cancellationToken: Ct));
        });

    [Fact]
    public async Task Readings_far_from_the_usual_level_alert_until_they_settle()
    {
        var (ownerId, owner) = await OwnerAsync("anomaly-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "anomaly-a-1", "web-1");
        await SeedHistoryAsync(hostId);
        await AddAnomalyRuleAsync(ownerId);

        await agent.SendSamplesAsync(LastFiveMinutes(12));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(6));
        await agent.SendSamplesAsync(LastFiveMinutes(60));
        await app.EvaluateAsync();

        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Firing, alert.Status);
        Assert.Equal(AlertCondition.Anomaly, alert.Condition);
        Assert.Equal("CPU usage on web-1 unusually high", alert.Title);
        Assert.Equal(60, alert.Value!.Value, precision: 3);
        Assert.InRange(alert.Baseline!.Value, 9, 11);

        app.Time.Advance(TimeSpan.FromMinutes(6));
        await agent.SendSamplesAsync(LastFiveMinutes(11));
        await app.EvaluateAsync();

        alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(AlertStatus.Resolved, alert.Status);
    }

    [Fact]
    public async Task New_hosts_have_no_usual_level_to_stray_from()
    {
        var (ownerId, owner) = await OwnerAsync("anomaly-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "anomaly-b-1");
        await AddAnomalyRuleAsync(ownerId);

        await agent.SendSamplesAsync(LastFiveMinutes(95));
        await app.EvaluateAsync();

        Assert.Empty(await app.AlertsForHostAsync(hostId));
    }
}
