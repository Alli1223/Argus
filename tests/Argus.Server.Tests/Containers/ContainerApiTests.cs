using System.Net;
using Argus.Contracts.Agent;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Containers;
using Argus.Server.Features.Metrics;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using static Argus.Server.Tests.Infrastructure.AgentTestHelpers;

namespace Argus.Server.Tests.Containers;

public sealed class ContainerApiTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetricSample Sample(DateTimeOffset time, ContainerReport? report, params ContainerUsage[] usage) =>
        MetricsIngestionTests.FullSample(time) with { Containers = report, ContainerUsage = usage };

    private static ContainerReport Report(params ContainerInfo[] containers) =>
        new() { EngineVersion = "29.8.1", Items = containers };

    private static ContainerInfo Web(int restarts = 0, string? health = "healthy") =>
        ContainerChangesTests.Container("shop-web-1", health: health, restarts: restarts) with
        {
            ComposeProject = "shop",
            ComposeService = "web",
            RestartPolicy = "unless-stopped",
            Ports = ["0.0.0.0:8080->80/tcp"],
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };

    private static ContainerUsage WebUsage(double cpu = 12.5) =>
        new() { Name = "shop-web-1", CpuPercent = cpu, MemoryBytes = 64_000_000, MemoryLimitBytes = 512_000_000, NetRxBytesPerSec = 1500, NetTxBytesPerSec = 300 };

    [Fact]
    public async Task Hosts_list_their_containers_with_what_they_use()
    {
        var owner = await app.CreateOwnerAsync("containers-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "containers-a-1", "docker-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(Sample(now.AddSeconds(-15), Report(Web(), ContainerChangesTests.Container("backup", "exited"))), Sample(now, null, WebUsage()));

        var containers = (await owner.GetJsonAsync<HostContainers>($"/api/hosts/{hostId}/containers"))!;

        Assert.Equal("29.8.1", containers.EngineVersion);
        Assert.Null(containers.Problem);
        Assert.Equal(["backup", "shop-web-1"], containers.Containers.Select(container => container.Name));
        var web = containers.Containers[1];
        Assert.Equal(("running", "healthy", "shop", "web"), (web.State, web.Health, web.ComposeProject, web.ComposeService));
        Assert.Equal(["0.0.0.0:8080->80/tcp"], web.Ports);
        Assert.Equal(12.5, web.Usage!.CpuPercent, precision: 3);
        Assert.Equal((64_000_000L, 512_000_000L, 1500d), (web.Usage.MemoryBytes, web.Usage.MemoryLimitBytes!.Value, web.Usage.NetRxBytesPerSec!.Value));
        Assert.Null(containers.Containers[0].Usage);
        Assert.Equal(1, containers.Containers[0].ExitCode);

        var fleet = (await owner.GetJsonAsync<List<ContainerHost>>("/api/containers"))!;
        Assert.Equal(("docker-1", 2), (Assert.Single(fleet).HostName, fleet[0].Containers.Count));
    }

    [Fact]
    public async Task Changes_between_reports_are_kept_as_events()
    {
        var owner = await app.CreateOwnerAsync("containers-b@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "containers-b-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(Sample(now.AddMinutes(-2), Report(Web(restarts: 1))));
        await agent.SendSamplesAsync(Sample(now.AddMinutes(-1), Report(Web(restarts: 5, health: "unhealthy"))));

        var detail = (await owner.GetJsonAsync<ContainerDetail>($"/api/hosts/{hostId}/containers/shop-web-1"))!;

        Assert.Equal(["unhealthy", "restarted"], detail.Events.Select(change => change.Kind).Order().Reverse());
        Assert.Equal(4, detail.Events.Single(change => change.Kind == "restarted").Count);
        Assert.Equal(4, detail.Container.RestartsLastHour);
        Assert.Equal(now.AddMinutes(-1), detail.Container.StateSince, TimeSpan.FromSeconds(1));
        Assert.False(detail.ActionsEnabled);
    }

    [Fact]
    public async Task Older_reports_change_nothing_and_problems_keep_the_containers_last_seen()
    {
        var owner = await app.CreateOwnerAsync("containers-c@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "containers-c-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(Sample(now.AddMinutes(-1), Report(Web())));
        await agent.SendSamplesAsync(Sample(now.AddMinutes(-3), Report()));

        Assert.Single((await owner.GetJsonAsync<HostContainers>($"/api/hosts/{hostId}/containers"))!.Containers);

        await agent.SendSamplesAsync(Sample(now, new ContainerReport { Problem = "permission denied" }));
        var containers = (await owner.GetJsonAsync<HostContainers>($"/api/hosts/{hostId}/containers"))!;
        Assert.Equal("permission denied", containers.Problem);
        Assert.Single(containers.Containers);
    }

    [Fact]
    public async Task Container_history_follows_its_usage()
    {
        var owner = await app.CreateOwnerAsync("containers-d@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "containers-d-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(Sample(now.AddMinutes(-2), Report(Web()), WebUsage(10)), Sample(now.AddMinutes(-1), null, WebUsage(30)));

        var series = (await owner.GetJsonAsync<MetricSeries>(
            $"/api/hosts/{hostId}/containers/shop-web-1/metrics?from={Iso(now.AddMinutes(-10))}&to={Iso(now.AddMinutes(1))}&points=20"))!;

        Assert.Equal(["cpu", "cpuMax", "memory", "memoryLimit", "netRx", "netTx"], series.Series.Keys.Order());
        Assert.Contains(series.Series["cpu"], value => value is > 29 and < 31);
        Assert.Contains(series.Series["netRx"], value => value is 1500);
    }

    [Fact]
    public async Task Containers_are_private_to_whoever_can_see_the_host()
    {
        var owner = await app.CreateOwnerAsync("containers-e@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "containers-e-1");
        await agent.SendSamplesAsync(Sample(DateTimeOffset.UtcNow, Report(Web())));
        var stranger = await app.CreateOwnerAsync("containers-f@example.com");
        var admin = await app.CreateOwnerAsync("containers-admin@example.com", Roles.Admin);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}/containers", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{hostId}/containers/shop-web-1", Ct)).StatusCode);
        Assert.Empty((await stranger.GetJsonAsync<List<ContainerHost>>("/api/containers"))!);
        Assert.Contains((await admin.GetJsonAsync<List<ContainerHost>>("/api/containers"))!, host => host.HostId == hostId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/hosts/{hostId}/containers/no-such-container", Ct)).StatusCode);
    }
}
