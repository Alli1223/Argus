namespace Argus.Contracts.Agent;

public enum HostPlatform
{
    Unknown,
    Linux,
    Windows,
    MacOS,
}

/// <summary>Hardware and operating system inventory of a monitored machine.</summary>
public sealed record SystemInfo
{
    public required string Hostname { get; init; }

    public required HostPlatform Platform { get; init; }

    /// <summary>Human readable OS name, e.g. "Ubuntu 24.04.1 LTS" or "Windows 11 Pro".</summary>
    public string? OsName { get; init; }

    public string? OsVersion { get; init; }

    public string? KernelVersion { get; init; }

    /// <summary>Processor architecture, e.g. "x64" or "arm64".</summary>
    public required string Architecture { get; init; }

    public string? CpuModel { get; init; }

    public int? CpuCores { get; init; }

    public int CpuLogicalProcessors { get; init; }

    public long MemoryTotalBytes { get; init; }

    public DateTimeOffset? BootTime { get; init; }

    public IReadOnlyList<string> IpAddresses { get; init; } = [];
}
