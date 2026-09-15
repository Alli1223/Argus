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
    long UptimeSeconds);

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
