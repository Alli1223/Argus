using System.Net;
using Argus.Contracts.Agent;
using Argus.Server.Features.Metrics;
using Argus.Server.Tests.Infrastructure;
using static Argus.Server.Tests.Infrastructure.AgentTestHelpers;

namespace Argus.Server.Tests.Metrics;

public sealed class FleetMetricsTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetricSample At(DateTimeOffset time, double cpu, double rx, double tx) =>
        MetricsIngestionTests.FullSample(time) with
        {
            Cpu = new CpuMetrics { UsagePercent = cpu },
            Network = new NetworkMetrics { RxBytesPerSec = rx, TxBytesPerSec = tx },
        };

    [Fact]
    public async Task Averages_usage_and_adds_up_traffic_across_the_hosts_someone_can_see()
    {
        var owner = await app.CreateOwnerAsync("fleet-a@example.com");
        var (webId, web) = await app.RegisterHostAsync(owner, "fleet-a-1", "web");
        var (dbId, database) = await app.RegisterHostAsync(owner, "fleet-a-2", "db");
        var stranger = await app.CreateOwnerAsync("fleet-b@example.com");
        var (_, other) = await app.RegisterHostAsync(stranger, "fleet-b-1", "theirs");

        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(-3);
        await web.SendSamplesAsync(At(minute.AddSeconds(10), cpu: 20, rx: 1_000, tx: 100));
        await database.SendSamplesAsync(At(minute.AddSeconds(20), cpu: 60, rx: 3_000, tx: 300));
        await other.SendSamplesAsync(At(minute.AddSeconds(30), cpu: 99, rx: 1_000_000, tx: 1_000_000));

        var fleet = await owner.GetJsonAsync<FleetMetrics>(
            $"/api/hosts/fleet/metrics?from={Iso(now.AddMinutes(-15))}&to={Iso(now.AddMinutes(1))}&points=16");

        var totals = fleet!.Totals;
        Assert.Equal("raw", totals.Resolution);
        var at = Array.FindIndex(totals.Series["hosts"], count => count is > 0);
        Assert.True(at >= 0);
        Assert.Equal(2, totals.Series["hosts"][at]);
        Assert.Equal(40, totals.Series["cpu"][at]!.Value, precision: 3);
        Assert.Equal(4_000, totals.Series["netRx"][at]!.Value, precision: 3);
        Assert.Equal(400, totals.Series["netTx"][at]!.Value, precision: 3);
        Assert.All(totals.Series.Values, values => Assert.Equal(totals.Time.Count, values.Length));

        // Buckets nobody reported in are gaps, not zeros.
        Assert.Contains(totals.Series["cpu"], value => value is null);

        Assert.Equal(["db", "web"], fleet.Hosts.Select(host => host.DisplayName));
        Assert.Equal(dbId, fleet.Hosts[0].HostId);
        Assert.Equal(webId, fleet.Hosts[1].HostId);
        Assert.Equal(20, fleet.Hosts[1].Cpu[at]!.Value, precision: 3);
    }

    [Fact]
    public async Task Long_ranges_come_from_rollups()
    {
        var owner = await app.CreateOwnerAsync("fleet-c@example.com");
        var (_, agent) = await app.RegisterHostAsync(owner, "fleet-c-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(At(now.AddMinutes(-1), cpu: 50, rx: 10, tx: 10));

        var fleet = await owner.GetJsonAsync<FleetMetrics>(
            $"/api/hosts/fleet/metrics?from={Iso(now.AddHours(-24))}&to={Iso(now.AddMinutes(1))}");

        Assert.Equal("5m", fleet!.Totals.Resolution);
        Assert.Contains(fleet.Totals.Series["cpu"], value => value is > 49 and < 51);
    }

    [Fact]
    public async Task Someone_without_hosts_gets_an_empty_series()
    {
        var owner = await app.CreateOwnerAsync("fleet-d@example.com");

        var fleet = await owner.GetJsonAsync<FleetMetrics>("/api/hosts/fleet/metrics");

        Assert.Empty(fleet!.Hosts);
        Assert.Empty(fleet.Totals.Time);
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected()
    {
        var owner = await app.CreateOwnerAsync("fleet-e@example.com");
        var now = DateTimeOffset.UtcNow;

        var response = await owner.GetAsync($"/api/hosts/fleet/metrics?from={Iso(now)}&to={Iso(now.AddHours(-1))}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Merging_keeps_gaps_and_skips_hosts_that_never_reported()
    {
        var hostA = Guid.NewGuid();
        var hostB = Guid.NewGuid();
        var t0 = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);
        var range = new SeriesRange(new DateTimeOffset(t0), new DateTimeOffset(t1.AddMinutes(1)), 10);

        var fleet = FleetMerge.Merge(
            [
                new FleetRow(t0, hostA, 10, 50, 1, 100, 10, null, null),
                new FleetRow(t0, hostB, null, null, null, null, null, null, null),
                new FleetRow(t1, hostA, null, null, null, null, null, null, null),
                new FleetRow(t1, hostB, null, null, null, null, null, null, null),
            ],
            new Dictionary<Guid, string> { [hostA] = "a", [hostB] = "b" },
            range,
            SeriesSource.Raw,
            TimeSpan.FromMinutes(1));

        Assert.Equal([10, null], fleet.Totals.Series["cpu"]);
        Assert.Equal([100, null], fleet.Totals.Series["netRx"]);
        Assert.Equal([null, null], fleet.Totals.Series["diskRead"]);
        Assert.Equal([1, 0], fleet.Totals.Series["hosts"]);
        Assert.Equal("a", Assert.Single(fleet.Hosts).DisplayName);
    }
}
