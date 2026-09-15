using System.ComponentModel.DataAnnotations;
using Argus.Contracts.Agent;

namespace Argus.Server.Features.Hosts;

public enum HostStatus
{
    Online,
    Offline,
}

/// <summary>The newest readings of a host, as shown in lists and dashboards.</summary>
public sealed record LatestMetrics(
    DateTimeOffset Time,
    double CpuPercent,
    double MemoryPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double? SwapPercent,
    double? Load1,
    double? DiskUsedPercent,
    double? NetRxBytesPerSec,
    double? NetTxBytesPerSec,
    long UptimeSeconds)
{
    /// <summary>The same summary built straight from a sample as it arrives, for live updates.</summary>
    public static LatestMetrics FromSample(MetricSample sample) => new(
        sample.Timestamp,
        sample.Cpu.UsagePercent,
        sample.Memory.TotalBytes > 0 ? 100.0 * sample.Memory.UsedBytes / sample.Memory.TotalBytes : 0,
        sample.Memory.UsedBytes,
        sample.Memory.TotalBytes,
        sample.Memory.SwapTotalBytes > 0 ? 100.0 * sample.Memory.SwapUsedBytes / sample.Memory.SwapTotalBytes : null,
        sample.Load?.Load1,
        sample.Filesystems
            .Where(fs => fs.UsedBytes + fs.AvailableBytes > 0)
            .Select(fs => (double?)(100.0 * fs.UsedBytes / (fs.UsedBytes + fs.AvailableBytes)))
            .Max(),
        sample.Network?.RxBytesPerSec,
        sample.Network?.TxBytesPerSec,
        sample.UptimeSeconds);
}

public sealed record HostSummary(
    Guid Id,
    string DisplayName,
    string Hostname,
    HostPlatform Platform,
    string? OsName,
    IReadOnlyList<string> Tags,
    HostStatus Status,
    DateTimeOffset? LastSeenAt,
    string AgentVersion,
    Guid OwnerId,
    LatestMetrics? Latest);

public sealed record HostDetail(
    Guid Id,
    string DisplayName,
    string Hostname,
    HostPlatform Platform,
    string? OsName,
    string? OsVersion,
    string? KernelVersion,
    string Architecture,
    string? CpuModel,
    int? CpuCores,
    int CpuLogicalProcessors,
    long MemoryTotalBytes,
    DateTimeOffset? BootTime,
    IReadOnlyList<string> IpAddresses,
    string AgentVersion,
    IReadOnlyList<string> Tags,
    string? Notes,
    HostStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? InventoryUpdatedAt,
    Guid OwnerId,
    LatestMetrics? Latest);

/// <summary>Partial update: only the properties that are present change.</summary>
public sealed record UpdateHostRequest
{
    [MinLength(1), MaxLength(256)]
    public string? DisplayName { get; init; }

    [MaxLength(HostTags.MaxTags)]
    public List<string>? Tags { get; init; }

    [MaxLength(4000)]
    public string? Notes { get; init; }
}
