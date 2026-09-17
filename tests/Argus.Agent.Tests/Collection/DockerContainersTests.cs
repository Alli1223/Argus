using System.Net;
using System.Text;
using System.Text.Json;
using Argus.Agent.Collection.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Argus.Agent.Tests.Collection;

/// <summary>Docker's answers, shaped like Docker 29's (API 1.56), with only the fields that matter here.</summary>
internal static class DockerFixtures
{
    public const string WebId = "c3cc787c3c4ee80c6125c80a0508d26d396612b0da40b793e29716e7786dc3d2";
    public const string JobId = "5d5be0cd07b4eb1813b7fb9bd3e2f0db0cc393b265c82a2750cb5473d1ca9c88";

    public static string List(string webState = "running") => $$$"""
        [
          {"Id": "{{{WebId}}}", "Names": ["/shop-web-1"], "Image": "nginx:1.29", "State": "{{{webState}}}",
           "Ports": [{"IP": "0.0.0.0", "PrivatePort": 80, "PublicPort": 8080, "Type": "tcp"},
                     {"IP": "::", "PrivatePort": 80, "PublicPort": 8080, "Type": "tcp"},
                     {"PrivatePort": 443, "Type": "tcp"}]},
          {"Id": "{{{JobId}}}", "Names": ["/backup"], "Image": "restic/restic", "State": "exited", "Ports": []}
        ]
        """;

    public static string WebInspect(string state = "running", string health = "healthy", int restarts = 2) => $$$"""
        {
          "Id": "{{{WebId}}}", "Name": "/shop-web-1", "Created": "2026-09-17T10:21:37.385880558Z", "RestartCount": {{{restarts}}},
          "State": {"Status": "{{{state}}}", "Running": {{{(state == "running" ? "true" : "false")}}}, "OOMKilled": false, "ExitCode": 0,
                    "StartedAt": "2026-09-17T10:21:38.826452637Z", "FinishedAt": "0001-01-01T00:00:00Z",
                    "Health": {"Status": "{{{health}}}", "FailingStreak": 0, "Log": []}},
          "Config": {"Image": "nginx:1.29", "Env": ["SECRET=do-not-read"],
                     "Labels": {"com.docker.compose.project": "shop", "com.docker.compose.service": "web"}},
          "HostConfig": {"RestartPolicy": {"Name": "unless-stopped", "MaximumRetryCount": 0}, "NetworkMode": "shop_default"}
        }
        """;

    public const string JobInspect = $$$"""
        {
          "Id": "{{{JobId}}}", "Name": "/backup", "Created": "2026-09-16T02:00:00Z", "RestartCount": 0,
          "State": {"Status": "exited", "Running": false, "OOMKilled": true, "ExitCode": 137,
                    "StartedAt": "2026-09-17T02:00:01Z", "FinishedAt": "2026-09-17T02:03:10Z"},
          "Config": {"Image": "restic/restic", "Labels": {}},
          "HostConfig": {"RestartPolicy": {"Name": "", "MaximumRetryCount": 0}, "NetworkMode": "host"}
        }
        """;

    /// <summary>cgroup v2 stats, as a one-shot reading gives them (no pre-reading).</summary>
    public static string Stats(ulong containerCpu, ulong systemCpu, ulong rx, ulong tx) => $$$"""
        {
          "read": "2026-09-17T10:50:02.295520199Z", "preread": "0001-01-01T00:00:00Z",
          "cpu_stats": {"cpu_usage": {"total_usage": {{{containerCpu}}} }, "system_cpu_usage": {{{systemCpu}}}, "online_cpus": 8},
          "precpu_stats": {"cpu_usage": {"total_usage": 0}},
          "memory_stats": {"usage": 240922624, "limit": 2147483648, "stats": {"inactive_file": 27455488, "anon": 136712192}},
          "networks": {"eth0": {"rx_bytes": {{{rx}}}, "tx_bytes": {{{tx}}} }, "eth1": {"rx_bytes": 1000, "tx_bytes": 0}}
        }
        """;
}

/// <summary>Answers Docker API paths from a table the test can change between readings.</summary>
internal sealed class FakeDocker : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode Status, string Body)> Routes { get; } = [];

    public int Requests { get; private set; }

    public void Answer(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Routes[pathAndQuery] = (status, json);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        var key = request.RequestUri!.PathAndQuery.TrimStart('/');
        var (status, body) = Routes.TryGetValue(key, out var route)
            ? route
            : (HttpStatusCode.NotFound, """{"message": "page not found"}""");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}

public sealed class DockerParsersTests
{
    [Fact]
    public void Containers_are_described_without_their_environment()
    {
        using var list = JsonDocument.Parse(DockerFixtures.List());
        using var web = JsonDocument.Parse(DockerFixtures.WebInspect());
        using var job = JsonDocument.Parse(DockerFixtures.JobInspect);

        var running = DockerParsers.ParseContainer(list.RootElement[0], web.RootElement);
        var finished = DockerParsers.ParseContainer(list.RootElement[1], job.RootElement);

        Assert.Equal(("shop-web-1", "nginx:1.29", "running", "healthy", 2), (running.Name, running.Image, running.State, running.Health, running.RestartCount));
        Assert.Null(running.ExitCode);
        Assert.Null(running.FinishedAt);
        // 10:21:38.826452637, to the tenth of a microsecond .NET keeps.
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 10, 21, 38, TimeSpan.Zero).AddTicks(8_264_526), running.StartedAt);
        Assert.Equal(("shop", "web", "unless-stopped"), (running.ComposeProject, running.ComposeService, running.RestartPolicy));
        Assert.Equal(["0.0.0.0:8080->80/tcp", ":::8080->80/tcp", "443/tcp"], running.Ports);
        Assert.DoesNotContain("do-not-read", JsonSerializer.Serialize(running, Argus.Contracts.Agent.AgentJsonContext.Default.ContainerInfo));

        Assert.Equal(("exited", 137, true, "no"), (finished.State, finished.ExitCode, finished.OomKilled, finished.RestartPolicy));
        Assert.Null(finished.Health);
        Assert.Null(finished.ComposeProject);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 2, 3, 10, TimeSpan.Zero), finished.FinishedAt);
    }

    [Fact]
    public void Usage_is_the_change_since_the_previous_reading()
    {
        using var before = JsonDocument.Parse(DockerFixtures.Stats(containerCpu: 1_000, systemCpu: 100_000, rx: 5_000, tx: 1_000));
        using var after = JsonDocument.Parse(DockerFixtures.Stats(containerCpu: 3_000, systemCpu: 140_000, rx: 35_000, tx: 1_600));

        var usage = DockerParsers.Usage("web", after.RootElement, DockerParsers.ParseCounters(before.RootElement), seconds: 10);

        Assert.Equal(5, usage.CpuPercent, precision: 6);
        Assert.Equal(240922624 - 27455488, usage.MemoryBytes);
        Assert.Equal(2147483648, usage.MemoryLimitBytes);
        Assert.Equal(3_000, usage.NetRxBytesPerSec);
        Assert.Equal(60, usage.NetTxBytesPerSec);
    }

    [Fact]
    public void Containers_on_the_host_network_have_no_traffic_of_their_own()
    {
        const string hostNetwork = """{"cpu_stats": {"cpu_usage": {"total_usage": 10}, "system_cpu_usage": 100}, "memory_stats": {}, "networks": null}""";
        using var stats = JsonDocument.Parse(hostNetwork);

        var usage = DockerParsers.Usage("job", stats.RootElement, new ContainerCounters(0, 0, null, null), seconds: 15);

        Assert.Null(usage.NetRxBytesPerSec);
        Assert.Null(usage.MemoryLimitBytes);
        Assert.Equal(10, usage.CpuPercent, precision: 6);
    }
}

public sealed class DockerContainersTests
{
    private static (DockerContainers Containers, FakeDocker Docker, FakeTimeProvider Time) Create(bool actions = false)
    {
        var docker = new FakeDocker();
        docker.Answer("version", """{"Version": "29.8.1", "ApiVersion": "1.56"}""");
        docker.Answer("containers/json?all=1", DockerFixtures.List());
        docker.Answer($"containers/{DockerFixtures.WebId}/json", DockerFixtures.WebInspect());
        docker.Answer($"containers/{DockerFixtures.JobId}/json", DockerFixtures.JobInspect);
        docker.Answer($"containers/{DockerFixtures.WebId}/stats?stream=false&one-shot=true", DockerFixtures.Stats(1_000, 100_000, 0, 0));
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-17T11:00:00Z"));
        var containers = new DockerContainers("", actions, time, NullLogger<DockerContainers>.Instance, new DockerClient(docker));
        return (containers, docker, time);
    }

    [Fact]
    public void Every_sample_carries_usage_and_the_list_goes_out_when_it_changes_or_each_minute()
    {
        var (containers, docker, time) = Create(actions: true);
        containers.Prime();
        docker.Answer($"containers/{DockerFixtures.WebId}/stats?stream=false&one-shot=true", DockerFixtures.Stats(2_000, 120_000, 0, 0));

        var first = containers.Collect();
        Assert.Equal("29.8.1", first.Report!.EngineVersion);
        Assert.True(first.Report.ActionsEnabled);
        Assert.Equal(["backup", "shop-web-1"], first.Report.Items.Select(item => item.Name));
        // Only running containers use anything; the stopped job has no usage.
        Assert.Equal(5, Assert.Single(first.Usage!).CpuPercent, precision: 6);

        time.Advance(TimeSpan.FromSeconds(15));
        var unchanged = containers.Collect();
        Assert.Null(unchanged.Report);
        Assert.Single(unchanged.Usage!);

        docker.Answer($"containers/{DockerFixtures.WebId}/json", DockerFixtures.WebInspect(health: "unhealthy"));
        time.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal("unhealthy", containers.Collect().Report!.Items.Single(item => item.Name == "shop-web-1").Health);

        time.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(containers.Collect().Report);
        time.Advance(DockerContainers.ReportInterval);
        Assert.NotNull(containers.Collect().Report);
    }

    [Fact]
    public void Containers_that_disappear_part_way_are_left_out()
    {
        var (containers, docker, _) = Create();
        docker.Routes.Remove($"containers/{DockerFixtures.JobId}/json");

        var reading = containers.Collect();

        Assert.Equal(["shop-web-1"], reading.Report!.Items.Select(item => item.Name));
    }

    [Fact]
    public void Problems_reaching_Docker_are_reported_once_a_minute()
    {
        var (containers, docker, time) = Create();
        docker.Answer("version", """{"message": "permission denied"}""", HttpStatusCode.Forbidden);

        var reading = containers.Collect();
        Assert.Equal("permission denied", reading.Report!.Problem);
        Assert.Empty(reading.Report.Items);
        Assert.Null(reading.Usage);

        time.Advance(TimeSpan.FromSeconds(15));
        Assert.Null(containers.Collect().Report);
    }
}
