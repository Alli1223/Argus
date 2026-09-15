using System.Runtime.Versioning;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemInfoSource : ISystemInfoSource
{
    public SystemInfo Collect()
    {
        var osRelease = LinuxFiles.ReadOrEmpty("/etc/os-release");
        var (osName, osVersion) = ProcParsers.ParseOsRelease(osRelease.Length > 0 ? osRelease : LinuxFiles.ReadOrEmpty("/usr/lib/os-release"));
        var cpu = ProcParsers.ParseCpuInfo(LinuxFiles.ReadOrEmpty("/proc/cpuinfo"));
        var uptime = LinuxFiles.ReadOrEmpty("/proc/uptime");
        var kernel = LinuxFiles.ReadOrEmpty("/proc/sys/kernel/osrelease").Trim();

        return new SystemInfo
        {
            Hostname = Environment.MachineName,
            Platform = HostPlatform.Linux,
            OsName = osName ?? "Linux",
            OsVersion = osVersion,
            KernelVersion = kernel.Length > 0 ? kernel : null,
            Architecture = AgentInfo.Architecture,
            CpuModel = cpu.Model,
            CpuCores = cpu.PhysicalCores,
            CpuLogicalProcessors = cpu.LogicalProcessors > 0 ? cpu.LogicalProcessors : Environment.ProcessorCount,
            MemoryTotalBytes = ProcParsers.ParseMeminfo(LinuxFiles.ReadOrEmpty("/proc/meminfo")).TotalBytes,
            BootTime = uptime.Length > 0 ? BootTime(ProcParsers.ParseUptimeSeconds(uptime)) : null,
            IpAddresses = NetworkAddresses.Collect(),
        };
    }

    public string? ReadMachineId() =>
        new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" }
            .Select(path => LinuxFiles.ReadOrEmpty(path).Trim())
            .FirstOrDefault(id => id.Length > 0);

    /// <summary>Boot time to the minute, so it does not wobble between inventory reports.</summary>
    private static DateTimeOffset BootTime(double uptimeSeconds)
    {
        var boot = DateTimeOffset.UtcNow.AddSeconds(-uptimeSeconds);
        return new DateTimeOffset(boot.Ticks - boot.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }
}
