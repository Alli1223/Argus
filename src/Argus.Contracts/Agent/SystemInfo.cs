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

    /// <summary>The machine's network interfaces, which Wake-on-LAN needs to reach it while it is off.</summary>
    public IReadOnlyList<NetworkInterfaceInfo> NetworkInterfaces { get; init; } = [];
}

public sealed record NetworkInterfaceInfo
{
    public required string Name { get; init; }

    /// <summary>The hardware address, as <c>aa:bb:cc:dd:ee:ff</c>.</summary>
    public required string MacAddress { get; init; }

    /// <summary>The interface's IPv4 addresses with their prefix length, such as <c>192.168.1.20/24</c>.</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];
}
