using Argus.Contracts.Agent;
using Argus.Server.Features.Metrics;

namespace Argus.Server.Tests.Metrics;

public class MetricsBatchValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(72);
    private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    internal static MetricSample Sample(DateTimeOffset timestamp, double cpu = 10) => new()
    {
        Timestamp = timestamp,
        Cpu = new CpuMetrics { UsagePercent = cpu },
        Memory = new MemoryMetrics { TotalBytes = 1000, UsedBytes = 400, AvailableBytes = 600 },
    };

    [Fact]
    public void Empty_and_oversized_batches_are_rejected()
    {
        Assert.NotNull(MetricsBatchValidator.ValidateShape(new MetricsBatch { Samples = [] }));
        Assert.NotNull(MetricsBatchValidator.ValidateShape(new MetricsBatch
        {
            Samples = Enumerable.Range(0, AgentLimits.MaxSamplesPerBatch + 1).Select(i => Sample(Now.AddSeconds(-i))).ToList(),
        }));
        Assert.Null(MetricsBatchValidator.ValidateShape(new MetricsBatch { Samples = [Sample(Now)] }));
    }

    [Fact]
    public void Samples_outside_the_time_window_are_dropped()
    {
        var samples = new[]
        {
            Sample(Now.AddHours(-73)),
            Sample(Now.AddHours(-71)),
            Sample(Now.AddMinutes(4)),
            Sample(Now.AddMinutes(6)),
        };

        var accepted = MetricsBatchValidator.Sanitize(samples, Now, MaxAge, MaxSkew, out var dropped);

        Assert.Equal(2, accepted.Count);
        Assert.Equal(2, dropped);
    }

    [Fact]
    public void Values_are_clamped_and_timestamps_normalised_to_utc()
    {
        var local = new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.FromHours(2));
        var sample = Sample(local, cpu: 100.4) with
        {
            Load = new LoadMetrics { Load1 = -1, Load5 = 1, Load15 = double.NaN },
            TopProcesses = [new ProcessMetrics { Pid = 1, Name = new string('x', 500), CpuPercent = -3, MemoryBytes = -1 }],
            Filesystems = [new FilesystemMetrics { MountPoint = " ", TotalBytes = 1, UsedBytes = 1, AvailableBytes = 0 }],
        };

        var cleaned = Assert.Single(MetricsBatchValidator.Sanitize([sample], Now, MaxAge, MaxSkew, out _));

        Assert.Equal(TimeSpan.Zero, cleaned.Timestamp.Offset);
        Assert.Equal(Now, cleaned.Timestamp);
        Assert.Equal(100, cleaned.Cpu.UsagePercent);
        Assert.Equal(0, cleaned.Load!.Load1);
        Assert.Equal(0, cleaned.Load.Load15);
        var process = Assert.Single(cleaned.TopProcesses!);
        Assert.Equal(256, process.Name.Length);
        Assert.Equal(0, process.CpuPercent);
        Assert.Equal(0, process.MemoryBytes);
        Assert.Empty(cleaned.Filesystems);
    }

    [Fact]
    public void Temperatures_keep_one_believable_reading_per_named_sensor()
    {
        var sample = Sample(Now) with
        {
            Temperatures =
            [
                new TemperatureMetrics { Device = "coretemp", Sensor = "Package id 0", Celsius = 61.5 },
                new TemperatureMetrics { Device = "coretemp", Sensor = "Package id 0", Celsius = 99 },
                new TemperatureMetrics { Device = " ", Sensor = "Core 0", Celsius = 50 },
                new TemperatureMetrics { Device = "acpitz", Sensor = "", Celsius = 50 },
                new TemperatureMetrics { Device = "acpitz", Sensor = "temp1", Celsius = double.NaN },
                new TemperatureMetrics { Device = "acpitz", Sensor = "temp2", Celsius = -300 },
                new TemperatureMetrics { Device = "ACPI/zone", Sensor = new string('x', 300), Celsius = 27.8 },
            ],
        };

        var cleaned = Assert.Single(MetricsBatchValidator.Sanitize([sample], Now, MaxAge, MaxSkew, out _));

        Assert.Equal(2, cleaned.Temperatures.Count);
        Assert.Equal(61.5, cleaned.Temperatures[0].Celsius);
        Assert.Equal("ACPI-zone", cleaned.Temperatures[1].Device);
        Assert.Equal(256, cleaned.Temperatures[1].Sensor.Length);
    }

    [Fact]
    public void Containers_are_named_once_with_states_argus_knows()
    {
        var sample = Sample(Now) with
        {
            Containers = new ContainerReport
            {
                Items =
                [
                    new ContainerInfo { Id = "a1", Name = "web", Image = "nginx", State = "running", Health = "healthy", RestartCount = -2 },
                    new ContainerInfo { Id = "a2", Name = "web", Image = "nginx", State = "exited" },
                    new ContainerInfo { Id = "b1", Name = "odd", Image = "x", State = "hibernating", Health = "sort of", RestartPolicy = "sometimes" },
                    new ContainerInfo { Id = "", Name = "nameless-id", Image = "x", State = "running" },
                ],
            },
            ContainerUsage =
            [
                new ContainerUsage { Name = "web", CpuPercent = 250, MemoryBytes = -1, MemoryLimitBytes = 0, NetRxBytesPerSec = double.NaN },
                new ContainerUsage { Name = " ", CpuPercent = 1 },
            ],
        };

        var cleaned = Assert.Single(MetricsBatchValidator.Sanitize([sample], Now, MaxAge, MaxSkew, out _));

        Assert.Equal(["web", "odd"], cleaned.Containers!.Items.Select(container => container.Name));
        Assert.Equal(("running", 0), (cleaned.Containers.Items[0].State, cleaned.Containers.Items[0].RestartCount));
        Assert.Equal(("unknown", (string?)null, (string?)null), (cleaned.Containers.Items[1].State, cleaned.Containers.Items[1].Health, cleaned.Containers.Items[1].RestartPolicy));
        var usage = Assert.Single(cleaned.ContainerUsage!);
        Assert.Equal((100d, 0L, (long?)null, (double?)0), (usage.CpuPercent, usage.MemoryBytes, usage.MemoryLimitBytes, usage.NetRxBytesPerSec));
    }

    [Fact]
    public void Oversized_lists_are_truncated()
    {
        var sample = Sample(Now) with
        {
            Interfaces = Enumerable.Range(0, AgentLimits.MaxInterfacesPerSample + 10)
                .Select(i => new NetworkInterfaceMetrics { Name = $"eth{i}" })
                .ToList(),
            Temperatures = Enumerable.Range(0, AgentLimits.MaxTemperaturesPerSample + 10)
                .Select(i => new TemperatureMetrics { Device = "coretemp", Sensor = $"Core {i}", Celsius = 40 })
                .ToList(),
        };

        var cleaned = Assert.Single(MetricsBatchValidator.Sanitize([sample], Now, MaxAge, MaxSkew, out _));

        Assert.Equal(AgentLimits.MaxInterfacesPerSample, cleaned.Interfaces.Count);
        Assert.Equal(AgentLimits.MaxTemperaturesPerSample, cleaned.Temperatures.Count);
    }
}
