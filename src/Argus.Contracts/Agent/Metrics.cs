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

    /// <summary>Set when someone asked this agent to update and the new build is ready to download.</summary>
    public AgentUpdateOffer? Update { get; init; }
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

    /// <summary>Temperature sensors the machine exposes; empty where it has none the agent can read.</summary>
    public IReadOnlyList<TemperatureMetrics> Temperatures { get; init; } = [];

    /// <summary>
    /// The machine's Docker containers, running or not. Only on samples where they changed, or at least
    /// a minute after the last report; null on other samples and from agents that do not watch Docker.
    /// </summary>
    public ContainerReport? Containers { get; init; }

    /// <summary>What each running container used over the interval; null from agents that do not watch Docker.</summary>
    public IReadOnlyList<ContainerUsage>? ContainerUsage { get; init; }

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

public sealed record TemperatureMetrics
{
    /// <summary>The chip, drive or zone the sensor belongs to, such as "coretemp", "nvme0" or "acpitz".</summary>
    public required string Device { get; init; }

    /// <summary>The sensor on that device, such as "Package id 0", "Core 3" or "Composite".</summary>
    public required string Sensor { get; init; }

    public required double Celsius { get; init; }
}

/// <summary>The containers on a machine, or why the agent could not list them.</summary>
public sealed record ContainerReport
{
    /// <summary>Docker's version, when the agent could reach it.</summary>
    public string? EngineVersion { get; init; }

    /// <summary>Why the agent could not read Docker, such as being refused access to its socket.</summary>
    public string? Problem { get; init; }

    /// <summary>Whether this machine lets Argus read container logs and start, stop and restart containers.</summary>
    public bool ActionsEnabled { get; init; }

    public IReadOnlyList<ContainerInfo> Items { get; init; } = [];
}

public sealed record ContainerInfo
{
    /// <summary>Docker's id, which changes when the container is recreated.</summary>
    public required string Id { get; init; }

    /// <summary>The name, without Docker's leading slash. It stays the same when Compose recreates a container.</summary>
    public required string Name { get; init; }

    public required string Image { get; init; }

    /// <summary>Docker's state: created, running, paused, restarting, removing, exited or dead.</summary>
    public required string State { get; init; }

    /// <summary>starting, healthy or unhealthy, for containers with a health check.</summary>
    public string? Health { get; init; }

    /// <summary>How often Docker restarted this container under its restart policy.</summary>
    public int RestartCount { get; init; }

    /// <summary>The exit code of the last run, for containers that have stopped.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Whether the last run was killed for running out of memory.</summary>
    public bool OomKilled { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>no, always, unless-stopped or on-failure.</summary>
    public string? RestartPolicy { get; init; }

    public string? ComposeProject { get; init; }

    public string? ComposeService { get; init; }

    /// <summary>Published ports, such as "0.0.0.0:8080->80/tcp".</summary>
    public IReadOnlyList<string> Ports { get; init; } = [];
}

public sealed record ContainerUsage
{
    public required string Name { get; init; }

    /// <summary>Share of the whole machine's CPU (0–100), like process CPU.</summary>
    public double CpuPercent { get; init; }

    /// <summary>Memory in use, not counting file cache the kernel can reclaim.</summary>
    public long MemoryBytes { get; init; }

    /// <summary>The container's memory limit, or the machine's memory when it has none.</summary>
    public long? MemoryLimitBytes { get; init; }

    /// <summary>Received traffic; null for containers without a network of their own, such as those on the host's.</summary>
    public double? NetRxBytesPerSec { get; init; }

    public double? NetTxBytesPerSec { get; init; }
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
