using System.Runtime.Versioning;
using System.Text.Json;
using Argus.Agent.Collection;
using Argus.Agent.Collection.Containers;
using Argus.Agent.Collection.Linux;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Collection;

/// <summary>Reads the real machine the tests run on.</summary>
[SupportedOSPlatform("linux")]
public class LinuxCollectorsTests
{
    private static readonly TimeSpan MeasuringInterval = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Metrics_are_read_from_this_machine()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var source = new LinuxMetricsSource(NullLogger<LinuxMetricsSource>.Instance);
        source.Prime();
        await Task.Delay(MeasuringInterval, TestContext.Current.CancellationToken);

        var reading = source.Collect();

        Assert.InRange(reading.Cpu.UsagePercent, 0, 100);
        Assert.True(reading.Memory.TotalBytes > 0);
        Assert.True(reading.UptimeSeconds > 0);
        Assert.NotNull(reading.Load);
        Assert.NotNull(reading.DiskIo);
        Assert.NotNull(reading.Network);
        Assert.Contains(reading.Filesystems, fs => fs.MountPoint == "/" && fs.TotalBytes > 0 && fs.UsedBytes > 0);
    }

    [Fact]
    public void System_info_describes_this_machine()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var source = new LinuxSystemInfoSource();

        var info = source.Collect();

        Assert.False(string.IsNullOrWhiteSpace(info.Hostname));
        Assert.Equal(HostPlatform.Linux, info.Platform);
        Assert.True(info.CpuLogicalProcessors > 0);
        Assert.True(info.MemoryTotalBytes > 0);
        Assert.NotNull(info.KernelVersion);
        Assert.NotNull(info.BootTime);
        Assert.NotNull(source.ReadMachineId());
    }

    [Fact]
    public async Task Processes_are_counted_and_ranked()
    {
        var collector = new ProcessCollector();
        collector.Prime();
        await Task.Delay(MeasuringInterval, TestContext.Current.CancellationToken);

        var (count, top) = collector.Collect(5);

        Assert.True(count > 5);
        Assert.InRange(top.Count, 1, 10);
        Assert.Contains(top, process => process.MemoryBytes > 0);
        Assert.All(top, process => Assert.InRange(process.CpuPercent, 0, 100));
    }

    [Fact]
    public void Temperatures_read_from_this_machine_are_believable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        // Containers and virtual machines often have no sensors, so only the readings found are checked.
        var readings = new LinuxTemperatures(includeDrives: false).Collect();

        Assert.All(readings, reading => Assert.InRange(reading.Celsius, -40, 125));
        Assert.Equal(readings.Count, readings.DistinctBy(reading => (reading.Device, reading.Sensor)).Count());
    }

    [Fact]
    public async Task Complete_samples_serialize_for_the_server()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var collector = new SampleCollector(
            new LinuxMetricsSource(NullLogger<LinuxMetricsSource>.Instance),
            new ProcessCollector(),
            new LinuxTemperatures(includeDrives: false),
            new NoContainers(),
            new NoServiceStatus(),
            TimeProvider.System);
        collector.Prime();
        await Task.Delay(MeasuringInterval, TestContext.Current.CancellationToken);

        var sample = collector.Collect();
        var json = JsonSerializer.Serialize(new MetricsBatch { Samples = [sample] }, AgentJsonContext.Default.MetricsBatch);
        var parsed = JsonSerializer.Deserialize(json, AgentJsonContext.Default.MetricsBatch);

        Assert.NotNull(parsed!.Samples[0].TopProcesses);
        Assert.NotEmpty(parsed.Samples[0].Filesystems);
    }
}
