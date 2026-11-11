using System.Net;
using Argus.Contracts.Agent;
using Argus.Server.Features.Summary;
using Argus.Server.Tests.Infrastructure;
using static Argus.Server.Tests.Infrastructure.AgentTestHelpers;

namespace Argus.Server.Tests.Metrics;

public sealed class SummaryTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetricSample At(DateTimeOffset time, double rx, double tx) =>
        MetricsIngestionTests.FullSample(time) with
        {
            Network = new NetworkMetrics { RxBytesPerSec = rx, TxBytesPerSec = tx },
        };

    [Fact]
    public async Task Adds_up_traffic_and_disk_activity_across_the_hosts_someone_can_see()
    {
        var owner = await app.CreateOwnerAsync("summary-a@example.com");
        var (webId, web) = await app.RegisterHostAsync(owner, "summary-a-1", "web");
        var (dbId, database) = await app.RegisterHostAsync(owner, "summary-a-2", "db");
        var stranger = await app.CreateOwnerAsync("summary-b@example.com");
        var (_, other) = await app.RegisterHostAsync(stranger, "summary-b-1", "theirs");

        var now = DateTimeOffset.UtcNow;
        // Four readings 15 seconds apart are a minute of reporting; two are half a minute.
        await web.SendSamplesAsync([.. Enumerable.Range(1, 4).Select(i => At(now.AddSeconds(-15 * i), rx: 1_000, tx: 100))]);
        await database.SendSamplesAsync([.. Enumerable.Range(1, 2).Select(i => At(now.AddSeconds(-15 * i), rx: 3_000, tx: 300))]);
        await other.SendSamplesAsync(At(now.AddSeconds(-15), rx: 1_000_000, tx: 1_000_000));

        var summary = await owner.GetJsonAsync<UsageSummary>("/api/summary?period=Day&count=2");

        Assert.Equal(SummaryPeriod.Day, summary!.Period);
        Assert.Equal("UTC", summary.TimeZone);
        Assert.Equal(["db", "web"], summary.Hosts.Select(host => host.DisplayName));
        Assert.Equal(2, summary.Periods.Count);
        Assert.True(summary.Periods[0].Start <= now && now < summary.Periods[0].End);
        Assert.Equal(summary.Periods[0].Start, summary.Periods[1].End);

        // The readings may straddle midnight, so add up both days.
        double Total(Func<UsageStats, double?> value) => summary.Periods.Sum(period => value(period.Totals) ?? 0);
        double HostTotal(Guid id, Func<UsageStats, double?> value) => summary.Periods
            .SelectMany(period => period.Hosts).Where(host => host.HostId == id).Sum(host => value(host.Stats) ?? 0);

        Assert.Equal(1_000 * 60 + 3_000 * 30, Total(stats => stats.NetworkReceivedBytes), precision: 3);
        Assert.Equal(100 * 60 + 300 * 30, Total(stats => stats.NetworkSentBytes), precision: 3);
        Assert.Equal(2_000 * 90, Total(stats => stats.DiskWriteBytes), precision: 3);
        Assert.Equal(1_000 * 60, HostTotal(webId, stats => stats.NetworkReceivedBytes), precision: 3);
        Assert.Equal(3_000 * 30, HostTotal(dbId, stats => stats.NetworkReceivedBytes), precision: 3);
        Assert.Equal(90, Total(stats => stats.ReportingSeconds), precision: 3);
        Assert.All(summary.Periods.SelectMany(period => period.Hosts), host => Assert.Contains(host.HostId, new[] { webId, dbId }));
    }

    [Fact]
    public async Task Months_follow_the_calendar_of_the_time_zone_asked_for()
    {
        var owner = await app.CreateOwnerAsync("summary-c@example.com");

        var summary = await owner.GetJsonAsync<UsageSummary>("/api/summary?period=Month&count=3&timeZone=Pacific/Auckland");

        Assert.Equal("Pacific/Auckland", summary!.TimeZone);
        Assert.Empty(summary.Hosts);
        Assert.Equal(3, summary.Periods.Count);
        Assert.True(summary.Periods[0].Start <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow < summary.Periods[0].End);
        Assert.Equal(summary.Periods[0].Start, summary.Periods[1].End);
        Assert.Equal(summary.Periods[1].Start, summary.Periods[2].End);
        // Auckland is 12 or 13 hours ahead, so its months start late in the UTC day before.
        Assert.All(summary.Periods, period => Assert.Contains(period.Start.UtcDateTime.Hour, new[] { 11, 12 }));
        Assert.All(summary.Periods, period => Assert.Empty(period.Hosts));
    }

    [Theory]
    [InlineData("period=Day&count=0")]
    [InlineData("period=Month&count=37")]
    [InlineData("timeZone=Not/AZone")]
    public async Task Invalid_requests_are_rejected(string query)
    {
        var owner = await app.CreateOwnerAsync($"summary-{Guid.NewGuid():N}@example.com");

        var response = await owner.GetAsync($"/api/summary?{query}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Merging_weights_averages_by_readings_and_counts_availability_from_when_a_host_was_added()
    {
        var hostA = Guid.NewGuid();
        var hostB = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var periods = new[] { new PeriodBounds(0, start, start.AddDays(1)) };
        var added = new Dictionary<Guid, DateTimeOffset>
        {
            [hostA] = start.AddDays(-30),
            [hostB] = start.AddHours(12),
        };

        var usage = SummaryMerge.Merge(
            periods,
            added,
            [
                new HostPeriodRow(0, hostA, 300, 43_200, 100, 200, 1_000, 10, 10, 50, 20, 30),
                new HostPeriodRow(0, hostB, 100, 43_200, 300, 400, 2_000, 20, 50, 90, 60, 80),
            ],
            [new HostPeriodTemperature(0, hostB, 71)],
            [new HostPeriodAlerts(0, hostA, 3, 1)],
            start.AddDays(2));

        var totals = Assert.Single(usage).Totals;
        Assert.Equal(400, totals.DiskReadBytes);
        Assert.Equal(3_000, totals.NetworkReceivedBytes);
        Assert.Equal(20, totals.CpuAveragePercent!.Value, precision: 6);
        Assert.Equal(90, totals.CpuPeakPercent);
        Assert.Equal(30, totals.MemoryAveragePercent!.Value, precision: 6);
        Assert.Equal(71, totals.TemperaturePeakCelsius);
        Assert.Equal(3, totals.Alerts);
        Assert.Equal(1, totals.CriticalAlerts);

        var stats = usage[0].Hosts.ToDictionary(host => host.HostId, host => host.Stats);
        Assert.Equal(50, stats[hostA].AvailabilityPercent!.Value, precision: 6);
        Assert.Equal(100, stats[hostB].AvailabilityPercent!.Value, precision: 6);
        Assert.Equal(75, totals.AvailabilityPercent!.Value, precision: 6);
    }

    [Fact]
    public void Hosts_that_did_nothing_in_a_period_are_left_out_of_it()
    {
        var host = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        var usage = SummaryMerge.Merge(
            [new PeriodBounds(0, start, start.AddDays(1)), new PeriodBounds(1, start.AddDays(-1), start)],
            new Dictionary<Guid, DateTimeOffset> { [host] = start.AddDays(-5) },
            [new HostPeriodRow(1, host, 4, 60, 1, 1, 1, 1, 1, 1, 1, 1)],
            [],
            [],
            start.AddHours(1));

        Assert.Empty(usage[0].Hosts);
        Assert.Null(usage[0].Totals.DiskReadBytes);
        Assert.Equal(host, Assert.Single(usage[1].Hosts).HostId);
    }
}
