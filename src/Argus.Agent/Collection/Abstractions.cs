using Argus.Contracts.Agent;

namespace Argus.Agent.Collection;

/// <summary>Everything in a sample except processes, read however the current OS allows.</summary>
internal sealed record SystemReading(
    CpuMetrics Cpu,
    MemoryMetrics Memory,
    LoadMetrics? Load,
    DiskIoMetrics? DiskIo,
    NetworkMetrics? Network,
    long UptimeSeconds,
    IReadOnlyList<FilesystemMetrics> Filesystems,
    IReadOnlyList<NetworkInterfaceMetrics> Interfaces);

internal interface ISystemMetricsSource
{
    /// <summary>Takes a first reading of cumulative counters so the next <see cref="Collect"/> can compute rates.</summary>
    void Prime();

    SystemReading Collect();
}

internal interface ISystemInfoSource
{
    SystemInfo Collect();

    /// <summary>The operating system's own machine identifier, if it has one.</summary>
    string? ReadMachineId();
}
