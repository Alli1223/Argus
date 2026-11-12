using System.Net;
using System.Text;
using System.Text.Json;
using Argus.Agent.Collection.Unifi;
using Argus.Agent.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Argus.Agent.Tests.Collection;

public sealed class UnifiParsersTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void Health_gives_internet_traffic_and_clients()
    {
        var health = UnifiParsers.ParseHealth(Json("""
            {"meta":{"rc":"ok"},"data":[
              {"subsystem":"wlan","num_user":20,"num_guest":2,"num_ap":3},
              {"subsystem":"wan","status":"ok","rx_bytes-r":125000.5,"tx_bytes-r":25000},
              {"subsystem":"www","rx_bytes-r":1,"tx_bytes-r":1},
              {"subsystem":"lan","num_user":9,"num_guest":0}]}
            """));

        Assert.Equal(new UnifiHealth(125000.5, 25000, 9, 22, 2), health);
        Assert.Equal(new UnifiHealth(null, null, null, null, null), UnifiParsers.ParseHealth(Json("""{"data":[]}""")));
    }

    [Fact]
    public void Devices_are_read_with_their_state_and_what_they_use()
    {
        var devices = UnifiParsers.ParseDevices(Json("""
            {"data":[
              {"mac":"aa:bb:cc:00:00:02","name":"Office AP","model":"U6LR","type":"uap","state":1,"uptime":86400,
               "system-stats":{"cpu":"4.5","mem":"38.1","uptime":"86400"},"uplink":{"rx_bytes-r":1000,"tx_bytes-r":5000},"num_sta":12},
              {"mac":"aa:bb:cc:00:00:01","model":"USW24","type":"usw","state":0},
              {"name":"no mac"}]}
            """));

        Assert.Equal(["Office AP", "USW24"], devices.Select(device => device.Name));
        var ap = devices[0];
        Assert.True(ap.Online);
        Assert.Equal((86400L, 4.5, 38.1, 1000.0, 5000.0, 12), (ap.UptimeSeconds!.Value, ap.CpuPercent!.Value, ap.MemoryPercent!.Value,
            ap.RxBytesPerSec!.Value, ap.TxBytesPerSec!.Value, ap.Clients!.Value));
        Assert.False(devices[1].Online);
        Assert.Null(devices[1].CpuPercent);
    }

    [Fact]
    public void Clients_downloaded_what_the_console_sent_them()
    {
        var clients = UnifiParsers.ParseClients(Json("""
            {"data":[
              {"mac":"11:22:33:44:55:66","hostname":"laptop","ip":"10.0.10.20","is_wired":false,"essid":"Home",
               "tx_bytes":5000000,"rx_bytes":200000},
              {"mac":"11:22:33:44:55:77","name":"NAS","hostname":"nas-host","is_wired":true,"wired-tx_bytes":900,"wired-rx_bytes":100},
              {"mac":"11:22:33:44:55:88"}]}
            """));

        Assert.Equal(new UnifiClientCounters("11:22:33:44:55:66", "laptop", "10.0.10.20", false, "Home", 5_000_000, 200_000), clients[0]);
        Assert.Equal(new UnifiClientCounters("11:22:33:44:55:77", "NAS", null, true, null, 900, 100), clients[1]);
        Assert.Equal("11:22:33:44:55:88", clients[2].Name);
    }

    [Fact]
    public void Site_and_version_come_from_the_integration_api()
    {
        Assert.Equal("9.4.19", UnifiParsers.Version(Json("""{"applicationVersion":"9.4.19"}""")));
        Assert.Equal("Home", UnifiParsers.SiteName(
            Json("""{"offset":0,"data":[{"id":"x","internalReference":"default","name":"Home"}]}"""), "default"));
    }
}

public sealed class UnifiNetworkTests
{
    private const string Health = """{"data":[{"subsystem":"wan","rx_bytes-r":100,"tx_bytes-r":10},{"subsystem":"lan","num_user":1}]}""";

    private static string Clients(long downloaded, long uploaded) =>
        $$"""{"data":[{"mac":"11:22:33:44:55:66","hostname":"laptop","tx_bytes":{{downloaded}},"rx_bytes":{{uploaded}}}]}""";

    [Fact]
    public void Client_traffic_is_what_the_counters_grew_by_between_reports()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-12T10:00:00Z"));
        var counters = (Downloaded: 1_000L, Uploaded: 100L);
        var api = new FakeApi(request => request.RequestUri!.AbsolutePath switch
        {
            "/proxy/network/api/s/default/stat/health" => (HttpStatusCode.OK, Health),
            "/proxy/network/api/s/default/stat/device" => (HttpStatusCode.OK, """{"data":[]}"""),
            "/proxy/network/api/s/default/stat/sta" => (HttpStatusCode.OK, Clients(counters.Downloaded, counters.Uploaded)),
            "/proxy/network/integration/v1/info" => (HttpStatusCode.OK, """{"applicationVersion":"9.4.19"}"""),
            "/proxy/network/integration/v1/sites" => (HttpStatusCode.OK, """{"data":[{"internalReference":"default","name":"Home"}]}"""),
            _ => (HttpStatusCode.NotFound, ""),
        });
        using var network = new UnifiNetwork(new UnifiClient(api, "https://10.0.10.1", "key"), "default", time, NullLogger<UnifiNetwork>.Instance);

        var first = network.Collect()!;
        Assert.Null(first.Problem);
        Assert.Equal((100.0, 10.0, 1), (first.WanRxBytesPerSec!.Value, first.WanTxBytesPerSec!.Value, first.WiredClients!.Value));
        Assert.Equal(("Home", "9.4.19"), (first.Site, first.Version));
        Assert.NotNull(first.Devices);
        Assert.Null(first.Clients);

        // Between detail reports, only the health figures.
        time.Advance(TimeSpan.FromSeconds(15));
        var between = network.Collect()!;
        Assert.Null(between.Devices);
        Assert.Null(between.Clients);

        time.Advance(UnifiNetwork.DetailInterval);
        counters = (61_000, 6_100);
        var second = network.Collect()!;
        var usage = Assert.Single(second.Clients!);
        Assert.Equal((60_000L, 6_000L, 75.0), (usage.DownloadedBytes, usage.UploadedBytes, usage.IntervalSeconds));
        Assert.All(api.Requests, request => Assert.Equal("key", request.Headers.GetValues("X-API-KEY").Single()));
    }

    [Fact]
    public void A_refused_key_is_reported_as_a_problem()
    {
        var api = new FakeApi(_ => (HttpStatusCode.Unauthorized, """{"error":"unauthorized"}"""));
        using var network = new UnifiNetwork(
            new UnifiClient(api, "https://10.0.10.1", "wrong"), "default", new FakeTimeProvider(), NullLogger<UnifiNetwork>.Instance);

        var report = network.Collect()!;

        Assert.Contains("refused the agent's API key", report.Problem);
        Assert.Null(report.WanRxBytesPerSec);
    }

    [Fact]
    public void Agents_read_unifi_only_with_an_address_and_a_key()
    {
        Assert.False(new AgentConfig { UnifiUrl = "https://10.0.10.1" }.ReadsUnifi);
        Assert.True(new AgentConfig { UnifiUrl = "https://10.0.10.1", UnifiApiKey = "k" }.ReadsUnifi);
        Assert.Contains(
            new AgentConfig { ServerUrl = "https://argus.example", UnifiUrl = "http://10.0.10.1", UnifiApiKey = "k" }.Validate(),
            error => error.StartsWith("UnifiUrl", StringComparison.Ordinal));
    }

    private sealed class FakeApi(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (status, body) = answer(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
