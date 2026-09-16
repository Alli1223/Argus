using System.Net;
using Argus.Contracts.Agent;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Argus.Server.Tests.Infrastructure;
using static Argus.Server.Tests.Infrastructure.AgentTestHelpers;

namespace Argus.Server.Tests.Metrics;

public sealed class MetricsQueryTests(ArgusAppFixture app) : IClassFixture<ArgusAppFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetricSample At(DateTimeOffset time, double cpu) =>
        MetricsIngestionTests.FullSample(time) with { Cpu = new CpuMetrics { UsagePercent = cpu } };

    [Fact]
    public async Task Recent_ranges_come_from_raw_samples_with_gaps_filled()
    {
        var owner = await app.CreateOwnerAsync("series-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "series-a-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(At(now.AddMinutes(-10), 10), At(now.AddMinutes(-9), 20), At(now.AddMinutes(-2), 90));

        var series = await owner.GetJsonAsync<MetricSeries>(
            $"/api/hosts/{hostId}/metrics?from={Iso(now.AddMinutes(-15))}&to={Iso(now.AddMinutes(1))}&points=16");

        Assert.Equal("raw", series!.Resolution);
        Assert.Equal(60, series.BucketSeconds);
        Assert.InRange(series.Time.Count, 16, 17);
        var cpu = series.Series["cpu"];
        Assert.Equal(series.Time.Count, cpu.Length);
        Assert.Contains(cpu, value => value is null);
        Assert.Contains(cpu, value => value is > 89 and < 91);
        Assert.Equal(3, cpu.Count(value => value is not null));
        Assert.All(series.Series.Values, values => Assert.Equal(series.Time.Count, values.Length));
    }

    [Theory]
    [InlineData(24, "5m", 300)]
    [InlineData(24 * 30, "1h", 3 * 3600)]
    public async Task Longer_ranges_come_from_rollups_including_recent_data(int hours, string resolution, int bucketSeconds)
    {
        var owner = await app.CreateOwnerAsync($"series-b-{hours}@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, $"series-b-{hours}");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(At(now.AddMinutes(-1), 50));

        var series = await owner.GetJsonAsync<MetricSeries>(
            $"/api/hosts/{hostId}/metrics?from={Iso(now.AddHours(-hours))}&to={Iso(now.AddMinutes(1))}&points=300");

        Assert.Equal(resolution, series!.Resolution);
        Assert.Equal(bucketSeconds, series.BucketSeconds);
        Assert.Contains(series.Series["cpu"], value => value is > 49 and < 51);
    }

    [Fact]
    public async Task Filesystems_network_and_processes_are_exposed()
    {
        var owner = await app.CreateOwnerAsync("series-c@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "series-c-1");
        var now = DateTimeOffset.UtcNow;
        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(now.AddMinutes(-1)), MetricsIngestionTests.FullSample(now));

        var filesystems = (await owner.GetJsonAsync<List<FilesystemSnapshot>>($"/api/hosts/{hostId}/filesystems"))!;
        Assert.Equal(["/", "/var"], filesystems.Select(fs => fs.MountPoint));
        Assert.Equal(40 / 95.0 * 100, filesystems[0].UsedPercent, precision: 3);

        var history = await owner.GetJsonAsync<MetricSeries>(
            $"/api/hosts/{hostId}/filesystems/history?from={Iso(now.AddMinutes(-10))}&to={Iso(now.AddMinutes(1))}&points=20");
        Assert.Equal(["/", "/var"], history!.Series.Keys.Order());

        var network = await owner.GetJsonAsync<MetricSeries>(
            $"/api/hosts/{hostId}/network?from={Iso(now.AddMinutes(-10))}&to={Iso(now.AddMinutes(1))}&points=20");
        Assert.Equal(["rx:eth0", "tx:eth0"], network!.Series.Keys.Order());
        Assert.Contains(network.Series["rx:eth0"], value => value is > 299 and < 301);

        var processes = await owner.GetJsonAsync<ProcessSnapshot>($"/api/hosts/{hostId}/processes");
        Assert.Equal("systemd", Assert.Single(processes!.Processes).Name);
    }

    [Theory]
    [InlineData(1, "raw")]
    [InlineData(24 * 30, "1h")]
    public async Task Temperatures_are_the_hottest_reading_of_each_sensor_per_point(int hours, string resolution)
    {
        var owner = await app.CreateOwnerAsync($"series-t-{hours}@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, $"series-t-{hours}");
        var now = DateTimeOffset.UtcNow;
        MetricSample Hot(DateTimeOffset time, double package) => MetricsIngestionTests.FullSample(time) with
        {
            Temperatures =
            [
                new TemperatureMetrics { Device = "coretemp", Sensor = "Package id 0", Celsius = package },
                new TemperatureMetrics { Device = "nvme0", Sensor = "Composite", Celsius = 41 },
            ],
        };
        // Buckets here are two minutes or a whole day, both aligned to even minutes, so these share one.
        var start = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 120 * 120 - 240);
        await agent.SendSamplesAsync(Hot(start.AddSeconds(10), 60), Hot(start.AddSeconds(25), 88));

        var series = await owner.GetJsonAsync<MetricSeries>(
            $"/api/hosts/{hostId}/temperatures?from={Iso(now.AddHours(-hours))}&to={Iso(now.AddMinutes(1))}&points=30");

        Assert.Equal(resolution, series!.Resolution);
        Assert.Equal(["coretemp/Package id 0", "nvme0/Composite"], series.Series.Keys.Order());
        Assert.Contains(series.Series["coretemp/Package id 0"], value => value is > 87.9 and < 88.1);
        Assert.DoesNotContain(series.Series["coretemp/Package id 0"], value => value is > 59 and < 61);
        Assert.All(series.Series.Values, values => Assert.Equal(series.Time.Count, values.Length));
    }

    [Fact]
    public async Task Fleet_temperatures_cover_the_visible_hosts_that_report_them()
    {
        var owner = await app.CreateOwnerAsync("series-f@example.com");
        var (warmId, warm) = await app.RegisterHostAsync(owner, "series-f-1", "warm");
        var (coldId, _) = await app.RegisterHostAsync(owner, "series-f-2", "cold");
        var stranger = await app.CreateOwnerAsync("series-g@example.com");
        var admin = await app.CreateOwnerAsync("series-admin@example.com", Roles.Admin);
        var now = DateTimeOffset.UtcNow;
        await warm.SendSamplesAsync(MetricsIngestionTests.FullSample(now));
        var range = $"from={Iso(now.AddHours(-1))}&to={Iso(now.AddMinutes(1))}&points=20";

        var fleet = (await owner.GetJsonAsync<List<HostTemperatures>>($"/api/hosts/temperatures?{range}"))!;
        var host = Assert.Single(fleet);
        Assert.Equal(warmId, host.HostId);
        Assert.Equal("warm", host.DisplayName);
        Assert.Equal(["coretemp/Package id 0", "nvme0/Composite"], host.History.Series.Keys.Order());

        var quiet = await owner.GetJsonAsync<MetricSeries>($"/api/hosts/{coldId}/temperatures?{range}");
        Assert.Empty(quiet!.Series);

        Assert.Empty((await stranger.GetJsonAsync<List<HostTemperatures>>($"/api/hosts/temperatures?{range}"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/hosts/{warmId}/temperatures", Ct)).StatusCode);
        Assert.Contains((await admin.GetJsonAsync<List<HostTemperatures>>($"/api/hosts/temperatures?{range}"))!, item => item.HostId == warmId);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/hosts/temperatures?points=5", Ct)).StatusCode);
    }

    [Fact]
    public async Task Hosts_without_process_data_answer_no_content()
    {
        var owner = await app.CreateOwnerAsync("series-d@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "series-d-1");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.GetAsync($"/api/hosts/{hostId}/processes", Ct)).StatusCode);
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected()
    {
        var owner = await app.CreateOwnerAsync("series-e@example.com");
        var (hostId, _) = await app.RegisterHostAsync(owner, "series-e-1");
        var now = DateTimeOffset.UtcNow;

        var reversed = await owner.GetAsync($"/api/hosts/{hostId}/metrics?from={Iso(now)}&to={Iso(now.AddHours(-1))}", Ct);
        var tooFewPoints = await owner.GetAsync($"/api/hosts/{hostId}/metrics?points=5", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooFewPoints.StatusCode);
    }
}
