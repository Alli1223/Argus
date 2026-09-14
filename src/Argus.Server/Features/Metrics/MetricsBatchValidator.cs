using Argus.Contracts.Agent;

namespace Argus.Server.Features.Metrics;

/// <summary>Checks and cleans agent batches before they reach the database.</summary>
public static class MetricsBatchValidator
{
    private const int NameLength = 256;
    private const int FsTypeLength = 64;

    /// <summary>Problems that make the whole batch unacceptable; the agent should not resend it.</summary>
    public static string? ValidateShape(MetricsBatch batch) => batch.Samples.Count switch
    {
        0 => "The batch contains no samples.",
        > AgentLimits.MaxSamplesPerBatch => $"A batch may contain at most {AgentLimits.MaxSamplesPerBatch} samples.",
        _ => null,
    };

    /// <summary>
    /// Returns the samples worth storing. Samples outside the accepted time window are dropped rather
    /// than failing the batch, so one bad reading cannot make an agent resend the same batch forever.
    /// Out-of-range values are clamped, oversized lists truncated and timestamps converted to UTC.
    /// </summary>
    public static List<MetricSample> Sanitize(
        IEnumerable<MetricSample> samples, DateTimeOffset now, TimeSpan maxAge, TimeSpan maxClockSkew, out int dropped)
    {
        var accepted = new List<MetricSample>();
        dropped = 0;

        foreach (var sample in samples)
        {
            if (sample.Timestamp < now - maxAge || sample.Timestamp > now + maxClockSkew)
            {
                dropped++;
                continue;
            }

            accepted.Add(Clean(sample));
        }

        return accepted;
    }

    private static MetricSample Clean(MetricSample sample) => sample with
    {
        Timestamp = sample.Timestamp.ToUniversalTime(),
        Cpu = new CpuMetrics
        {
            UsagePercent = Percent(sample.Cpu.UsagePercent),
            UserPercent = Percent(sample.Cpu.UserPercent),
            SystemPercent = Percent(sample.Cpu.SystemPercent),
            IowaitPercent = Percent(sample.Cpu.IowaitPercent),
            StealPercent = Percent(sample.Cpu.StealPercent),
        },
        Memory = new MemoryMetrics
        {
            TotalBytes = Math.Max(0, sample.Memory.TotalBytes),
            UsedBytes = Math.Max(0, sample.Memory.UsedBytes),
            AvailableBytes = Math.Max(0, sample.Memory.AvailableBytes),
            CachedBytes = sample.Memory.CachedBytes is { } cached ? Math.Max(0, cached) : null,
            SwapTotalBytes = Math.Max(0, sample.Memory.SwapTotalBytes),
            SwapUsedBytes = Math.Max(0, sample.Memory.SwapUsedBytes),
        },
        Load = sample.Load is { } load
            ? new LoadMetrics { Load1 = Rate(load.Load1), Load5 = Rate(load.Load5), Load15 = Rate(load.Load15) }
            : null,
        DiskIo = sample.DiskIo is { } disk
            ? new DiskIoMetrics
            {
                ReadBytesPerSec = Rate(disk.ReadBytesPerSec),
                WriteBytesPerSec = Rate(disk.WriteBytesPerSec),
                ReadOpsPerSec = Rate(disk.ReadOpsPerSec),
                WriteOpsPerSec = Rate(disk.WriteOpsPerSec),
                UtilizationPercent = Percent(disk.UtilizationPercent),
            }
            : null,
        Network = sample.Network is { } network
            ? new NetworkMetrics { RxBytesPerSec = Rate(network.RxBytesPerSec), TxBytesPerSec = Rate(network.TxBytesPerSec) }
            : null,
        ProcessCount = Math.Max(0, sample.ProcessCount),
        UptimeSeconds = Math.Max(0, sample.UptimeSeconds),
        Filesystems = sample.Filesystems
            .Where(fs => !string.IsNullOrWhiteSpace(fs.MountPoint))
            .Take(AgentLimits.MaxFilesystemsPerSample)
            .Select(fs => new FilesystemMetrics
            {
                MountPoint = Clip(fs.MountPoint, NameLength),
                Device = ClipOptional(fs.Device, NameLength),
                FsType = ClipOptional(fs.FsType, FsTypeLength),
                TotalBytes = Math.Max(0, fs.TotalBytes),
                UsedBytes = Math.Max(0, fs.UsedBytes),
                AvailableBytes = Math.Max(0, fs.AvailableBytes),
                InodesTotal = fs.InodesTotal is { } total ? Math.Max(0, total) : null,
                InodesUsed = fs.InodesUsed is { } used ? Math.Max(0, used) : null,
            })
            .ToList(),
        Interfaces = sample.Interfaces
            .Where(nic => !string.IsNullOrWhiteSpace(nic.Name))
            .Take(AgentLimits.MaxInterfacesPerSample)
            .Select(nic => new NetworkInterfaceMetrics
            {
                Name = Clip(nic.Name, NameLength),
                RxBytesPerSec = Rate(nic.RxBytesPerSec),
                TxBytesPerSec = Rate(nic.TxBytesPerSec),
                RxPacketsPerSec = Rate(nic.RxPacketsPerSec),
                TxPacketsPerSec = Rate(nic.TxPacketsPerSec),
                RxErrorsPerSec = Rate(nic.RxErrorsPerSec),
                TxErrorsPerSec = Rate(nic.TxErrorsPerSec),
            })
            .ToList(),
        TopProcesses = sample.TopProcesses?
            .Take(AgentLimits.MaxTopProcesses)
            .Select(process => new ProcessMetrics
            {
                Pid = process.Pid,
                Name = Clip(process.Name, NameLength),
                CpuPercent = Percent(process.CpuPercent),
                MemoryBytes = Math.Max(0, process.MemoryBytes),
            })
            .ToList(),
    };

    private static double Percent(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;

    private static double? Percent(double? value) => value is { } v ? Percent(v) : null;

    private static double Rate(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static string Clip(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private static string? ClipOptional(string? value, int maxLength) => value is null ? null : Clip(value, maxLength);
}
