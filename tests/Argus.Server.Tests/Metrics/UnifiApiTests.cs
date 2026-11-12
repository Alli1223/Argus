using System.Net;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Metrics;
using Argus.Server.Features.Summary;
using Argus.Server.Features.Unifi;
using Argus.Server.Tests.Infrastructure;
using static Argus.Server.Tests.Infrastructure.AgentTestHelpers;

namespace Argus.Server.Tests.Metrics;

public sealed class UnifiApiTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static UnifiDevice Device(string mac, string name, bool online) => new()
    {
        Mac = mac, Name = name, Model = "U6LR", Type = "uap", Online = online, UptimeSeconds = 3600, CpuPercent = 5, MemoryPercent = 40,
        Clients = 3,
    };

    internal static MetricSample Network(
        DateTimeOffset time, double rx, double tx, IReadOnlyList<UnifiDevice>? devices = null, IReadOnlyList<UnifiClientUsage>? clients = null) =>
        MetricsIngestionTests.FullSample(time) with
        {
            Unifi = new UnifiReport
            {
                Site = "Home",
                Version = "9.4.19",
                WanRxBytesPerSec = rx,
                WanTxBytesPerSec = tx,
                WiredClients = 4,
                WirelessClients = 10,
                GuestClients = 1,
                Devices = devices,
                Clients = clients,
            },
        };

    private static UnifiClientUsage Client(string mac, string name, long downloaded, long uploaded) => new()
    {
        Mac = mac, Name = name, Ip = "10.0.10.20", DownloadedBytes = downloaded, UploadedBytes = uploaded, IntervalSeconds = 60,
    };

    [Fact]
    public async Task A_network_is_listed_with_its_traffic_devices_and_clients()
    {
        var owner = await app.CreateOwnerAsync("unifi-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "unifi-a-1", "gateway-reader");
        var (otherId, _) = await app.RegisterHostAsync(owner, "unifi-a-2", "plain");
        var stranger = await app.CreateOwnerAsync("unifi-b@example.com");

        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(
            Network(now.AddSeconds(-75), 1_000, 100,
                [Device("AA:BB:CC:00:00:01", "Office AP", true), Device("aa:bb:cc:00:00:02", "Garage AP", false)],
                [Client("11:22:33:44:55:66", "laptop", 600_000, 60_000), Client("11:22:33:44:55:77", "tv", 6_000_000, 1_000)]),
            Network(now.AddSeconds(-60), 3_000, 300),
            Network(now.AddSeconds(-15), 2_000, 200,
                [Device("aa:bb:cc:00:00:01", "Office AP", true)],
                [Client("11:22:33:44:55:66", "laptop", 400_000, 40_000)]));

        var networks = (await owner.GetJsonAsync<List<UnifiNetworkResponse>>("/api/unifi"))!;
        var network = Assert.Single(networks);
        Assert.Equal((hostId, "gateway-reader", "Home", "9.4.19"), (network.HostId, network.HostName, network.Site, network.Version));
        Assert.Null(network.Problem);
        Assert.Equal((2_000.0, 200.0, 4, 10), (network.Now!.WanRxBytesPerSec!.Value, network.Now.WanTxBytesPerSec!.Value,
            network.Now.WiredClients!.Value, network.Now.WirelessClients!.Value));
        // The newest device report replaces the last: the garage AP is gone.
        var device = Assert.Single(network.Devices);
        Assert.Equal(("aa:bb:cc:00:00:01", "Office AP", true), (device.Mac, device.Name, device.Online));
        Assert.DoesNotContain(networks, item => item.HostId == otherId);
        Assert.Empty((await stranger.GetJsonAsync<List<UnifiNetworkResponse>>("/api/unifi"))!);

        var history = (await owner.GetJsonAsync<MetricSeries>(
            $"/api/unifi/{hostId}/history?from={Iso(now.AddMinutes(-10))}&to={Iso(now.AddMinutes(1))}&points=11"))!;
        Assert.Equal("raw", history.Resolution);
        Assert.Contains(history.Series["wanRx"], value => value is > 0);
        Assert.Contains(history.Series["wireless"], value => value is 10);

        var clients = (await owner.GetJsonAsync<List<UnifiClientTotal>>(
            $"/api/unifi/{hostId}/clients?from={Iso(now.AddMinutes(-10))}&to={Iso(now.AddMinutes(1))}"))!;
        Assert.Equal(["tv", "laptop"], clients.Select(client => client.Name));
        Assert.Equal((1_000_000L, 100_000L), (clients[1].DownloadedBytes, clients[1].UploadedBytes));

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/unifi/{hostId}/clients", Ct)).StatusCode);
    }

    [Fact]
    public async Task The_summary_adds_up_internet_traffic()
    {
        var owner = await app.CreateOwnerAsync("unifi-c@example.com");
        var (_, agent) = await app.RegisterHostAsync(owner, "unifi-c-1");
        var now = DateTimeOffset.UtcNow;
        // Four readings 15 seconds apart are a minute of reporting.
        await agent.SendSamplesAsync([.. Enumerable.Range(1, 4).Select(i => Network(now.AddSeconds(-15 * i), rx: 1_000, tx: 100))]);

        var summary = (await owner.GetJsonAsync<UsageSummary>("/api/summary?period=Day&count=2"))!;

        Assert.Equal(1_000 * 60, summary.Periods.Sum(period => period.Internet?.DownloadedBytes ?? 0), precision: 3);
        Assert.Equal(100 * 60, summary.Periods.Sum(period => period.Internet?.UploadedBytes ?? 0), precision: 3);
    }

    [Fact]
    public async Task A_console_that_refuses_the_agent_is_reported_without_readings()
    {
        var owner = await app.CreateOwnerAsync("unifi-d@example.com");
        var (_, agent) = await app.RegisterHostAsync(owner, "unifi-d-1");
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(DateTimeOffset.UtcNow.AddSeconds(-5)) with
        {
            Unifi = new UnifiReport { Problem = "The UniFi console refused the agent's API key." },
        });

        var network = Assert.Single((await owner.GetJsonAsync<List<UnifiNetworkResponse>>("/api/unifi"))!);

        Assert.Equal("The UniFi console refused the agent's API key.", network.Problem);
        Assert.Null(network.Now);
        Assert.Empty(network.Devices);
    }
}

public sealed class UnifiAlertTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private DateTimeOffset Now => app.Time.GetUtcNow();

    [Fact]
    public async Task An_offline_device_alerts_after_the_rule_duration_and_resolves_when_back()
    {
        var owner = await app.CreateOwnerAsync("unifi-alerts-a@example.com");
        var ownerId = (await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id;
        var (hostId, agent) = await app.RegisterHostAsync(owner, "unifi-alerts-a-1", "reader");
        await app.AddRuleAsync(ownerId, AlertMetric.UnifiDeviceOffline, threshold: 0, durationSeconds: 120);

        await agent.SendSamplesAsync(UnifiApiTests.Network(Now.AddSeconds(-5), 1, 1,
            [UnifiApiTests.Device("aa:00", "Garage AP", false), UnifiApiTests.Device("aa:01", "Office AP", true)]));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(3));
        await agent.SendSamplesAsync(UnifiApiTests.Network(Now.AddSeconds(-5), 1, 1,
            [UnifiApiTests.Device("aa:00", "Garage AP", false), UnifiApiTests.Device("aa:01", "Office AP", true)]));
        await app.EvaluateAsync();

        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(("Garage AP", "UniFi device Garage AP is offline", AlertStatus.Firing), (alert.ResourceKey, alert.Title, alert.Status));

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(UnifiApiTests.Network(Now.AddSeconds(-5), 1, 1,
            [UnifiApiTests.Device("aa:00", "Garage AP", true), UnifiApiTests.Device("aa:01", "Office AP", true)]));
        await app.EvaluateAsync();

        Assert.Equal(AlertStatus.Resolved, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);
    }
}
