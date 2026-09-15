namespace Argus.Contracts.Agent;

/// <summary>A batch of samples posted by an agent. Re-sending a sample is harmless (idempotent).</summary>
public sealed record MetricsBatch
{
    public required IReadOnlyList<MetricSample> Samples { get; init; }
}

public sealed record MetricsBatchResponse
{
    /// <summary>Number of new samples stored (duplicates are ignored).</summary>
    public int Accepted { get; init; }

    public AgentSettings? Settings { get; init; }
}

/// <summary>
/// One point-in-time reading of a machine. Rates are per second and averaged over the interval
/// since the previous sample; percentages are 0–100 of total machine capacity.
/// </summary>
public sealed record MetricSample
{
    public required DateTimeOffset Timestamp { get; init; }

    public required CpuMetrics Cpu { get; init; }

    public required MemoryMetrics Memory { get; init; }

    /// <summary>Load averages; only reported on Unix-like systems.</summary>
    public LoadMetrics? Load { get; init; }

    /// <summary>Aggregate IO of physical disks.</summary>
    public DiskIoMetrics? DiskIo { get; init; }

    /// <summary>Aggregate traffic of all non-loopback interfaces.</summary>
    public NetworkMetrics? Network { get; init; }

    public int ProcessCount { get; init; }

    public long UptimeSeconds { get; init; }

    public IReadOnlyList<FilesystemMetrics> Filesystems { get; init; } = [];

    public IReadOnlyList<NetworkInterfaceMetrics> Interfaces { get; init; } = [];

    /// <summary>Busiest processes; agents may only attach this to the newest sample of a batch.</summary>
    public IReadOnlyList<ProcessMetrics>? TopProcesses { get; init; }

    /// <summary>
    /// Services that should be running but are not: failed systemd units, or stopped Windows services
    /// set to start automatically. Only on samples where the agent checked (about once a minute); an
    /// empty list means every service it watches is fine.
    /// </summary>
    public IReadOnlyList<ServiceProblem>? FailedServices { get; init; }
}

public sealed record ServiceProblem
{
    /// <summary>The service's name, such as "nginx.service" or "Spooler".</summary>
    public required string Name { get; init; }

    /// <summary>What the service is, in words, when the system says.</summary>
    public string? Description { get; init; }

    /// <summary>What is wrong, in the system's own word: "failed" or "stopped".</summary>
    public required string State { get; init; }
}

public sealed record CpuMetrics
{
    public required double UsagePercent { get; init; }

    public double? UserPercent { get; init; }

    public double? SystemPercent { get; init; }

    public double? IowaitPercent { get; init; }

    public double? StealPercent { get; init; }
}

public sealed record MemoryMetrics
{
    public required long TotalBytes { get; init; }

    /// <summary>Memory in use by applications (total minus available).</summary>
    public required long UsedBytes { get; init; }

    public required long AvailableBytes { get; init; }

    /// <summary>Page cache and buffers, where the OS reports it.</summary>
    public long? CachedBytes { get; init; }

    public long SwapTotalBytes { get; init; }

    public long SwapUsedBytes { get; init; }
}

public sealed record LoadMetrics
{
    public required double Load1 { get; init; }

    public required double Load5 { get; init; }

    public required double Load15 { get; init; }
}

public sealed record DiskIoMetrics
{
    public double ReadBytesPerSec { get; init; }

    public double WriteBytesPerSec { get; init; }

    public double ReadOpsPerSec { get; init; }

    public double WriteOpsPerSec { get; init; }

    /// <summary>Share of time the busiest disk was servicing requests.</summary>
    public double? UtilizationPercent { get; init; }
}

public sealed record NetworkMetrics
{
    public double RxBytesPerSec { get; init; }

    public double TxBytesPerSec { get; init; }
}

public sealed record FilesystemMetrics
{
    /// <summary>Mount point on Unix ("/var"), drive root on Windows ("C:\").</summary>
    public required string MountPoint { get; init; }

    public string? Device { get; init; }

    public string? FsType { get; init; }

    public required long TotalBytes { get; init; }

    public required long UsedBytes { get; init; }

    /// <summary>Space available to unprivileged users (excludes reserved blocks).</summary>
    public required long AvailableBytes { get; init; }

    public long? InodesTotal { get; init; }

    public long? InodesUsed { get; init; }
}

public sealed record NetworkInterfaceMetrics
{
    public required string Name { get; init; }

    public double RxBytesPerSec { get; init; }

    public double TxBytesPerSec { get; init; }

    public double RxPacketsPerSec { get; init; }

    public double TxPacketsPerSec { get; init; }

    public double RxErrorsPerSec { get; init; }

    public double TxErrorsPerSec { get; init; }
}

public sealed record ProcessMetrics
{
    public required int Pid { get; init; }

    public required string Name { get; init; }

    /// <summary>CPU share of total machine capacity (0–100), like Windows Task Manager.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Resident set size / working set.</summary>
    public long MemoryBytes { get; init; }
}
