using System.Text.Json;
using Argus.Contracts.Agent;

namespace Argus.Server.Tests.Contracts;

public class AgentJsonContextTests
{
    private static MetricsBatch SampleBatch() => new()
    {
        Samples =
        [
            new MetricSample
            {
                Timestamp = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero),
                Cpu = new CpuMetrics { UsagePercent = 42.5, UserPercent = 30, SystemPercent = 12.5, IowaitPercent = 0.25 },
                Memory = new MemoryMetrics
                {
                    TotalBytes = 16_000_000_000,
                    UsedBytes = 6_000_000_000,
                    AvailableBytes = 10_000_000_000,
                    CachedBytes = 4_000_000_000,
                    SwapTotalBytes = 2_000_000_000,
                    SwapUsedBytes = 1_000_000,
                },
                Load = new LoadMetrics { Load1 = 1.5, Load5 = 1.2, Load15 = 0.9 },
                DiskIo = new DiskIoMetrics { ReadBytesPerSec = 1024, WriteBytesPerSec = 2048, ReadOpsPerSec = 3, WriteOpsPerSec = 4, UtilizationPercent = 5 },
                Network = new NetworkMetrics { RxBytesPerSec = 1000, TxBytesPerSec = 500 },
                ProcessCount = 321,
                UptimeSeconds = 86_400,
                Filesystems =
                [
                    new FilesystemMetrics
                    {
                        MountPoint = "/",
                        Device = "/dev/sda1",
                        FsType = "ext4",
                        TotalBytes = 100,
                        UsedBytes = 40,
                        AvailableBytes = 55,
                        InodesTotal = 1000,
                        InodesUsed = 10,
                    },
                ],
                Interfaces = [new NetworkInterfaceMetrics { Name = "eth0", RxBytesPerSec = 1000, TxBytesPerSec = 500, RxPacketsPerSec = 10, TxPacketsPerSec = 5 }],
                TopProcesses = [new ProcessMetrics { Pid = 1, Name = "systemd", CpuPercent = 0.1, MemoryBytes = 12_345 }],
            },
        ],
    };

    [Fact]
    public void MetricsBatch_round_trips()
    {
        var json = JsonSerializer.Serialize(SampleBatch(), AgentJsonContext.Default.MetricsBatch);
        var parsed = JsonSerializer.Deserialize(json, AgentJsonContext.Default.MetricsBatch);

        Assert.NotNull(parsed);
        Assert.Equal(json, JsonSerializer.Serialize(parsed, AgentJsonContext.Default.MetricsBatch));
        var sample = Assert.Single(parsed.Samples);
        Assert.Equal(42.5, sample.Cpu.UsagePercent);
        Assert.Equal("/", Assert.Single(sample.Filesystems).MountPoint);
        Assert.Equal("eth0", Assert.Single(sample.Interfaces).Name);
    }

    [Fact]
    public void Uses_camel_case_and_omits_nulls()
    {
        var sample = SampleBatch().Samples[0] with { Load = null, TopProcesses = null };

        var json = JsonSerializer.Serialize(new MetricsBatch { Samples = [sample] }, AgentJsonContext.Default.MetricsBatch);

        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement.GetProperty("samples")[0];
        Assert.True(element.GetProperty("cpu").TryGetProperty("usagePercent", out _));
        Assert.False(element.TryGetProperty("load", out _));
        Assert.False(element.TryGetProperty("topProcesses", out _));
    }

    [Fact]
    public void Enums_are_serialized_as_strings_and_parsed_case_insensitively()
    {
        var request = new RegisterAgentRequest
        {
            EnrollmentToken = "argus_et_x",
            MachineId = "abc",
            AgentVersion = "1.0.0",
            SystemInfo = new SystemInfo { Hostname = "web-1", Platform = HostPlatform.Linux, Architecture = "x64" },
        };

        var json = JsonSerializer.Serialize(request, AgentJsonContext.Default.RegisterAgentRequest);
        var parsed = JsonSerializer.Deserialize(
            """{"enrollmentToken":"t","machineId":"m","agentVersion":"1","systemInfo":{"hostname":"h","platform":"windows","architecture":"arm64"}}""",
            AgentJsonContext.Default.RegisterAgentRequest);

        Assert.Contains("\"platform\":\"Linux\"", json);
        Assert.Equal(HostPlatform.Windows, parsed!.SystemInfo.Platform);
    }

    [Fact]
    public void Missing_required_properties_are_rejected()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("""{"samples":[{"cpu":{"usagePercent":1}}]}""", AgentJsonContext.Default.MetricsBatch));
    }
}
