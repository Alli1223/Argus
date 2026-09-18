using System.Runtime.Versioning;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Linux;

[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemInfoSource(HostPaths? paths = null) : ISystemInfoSource
{
    private readonly HostPaths _paths = paths ?? HostPaths.Local;

    public SystemInfo Collect()
    {
        var osRelease = LinuxFiles.ReadOrEmpty($"{_paths.Etc}/os-release");
        var (osName, osVersion) = ProcParsers.ParseOsRelease(osRelease.Length > 0 ? osRelease : LinuxFiles.ReadOrEmpty($"{_paths.Root}/usr/lib/os-release"));
        var cpu = ProcParsers.ParseCpuInfo(LinuxFiles.ReadOrEmpty(_paths.InProc("cpuinfo")));
        var uptime = LinuxFiles.ReadOrEmpty(_paths.InProc("uptime"));
        var kernel = LinuxFiles.ReadOrEmpty(_paths.InProc("sys/kernel/osrelease")).Trim();

        return new SystemInfo
        {
            Hostname = Hostname(),
            Platform = HostPlatform.Linux,
            OsName = osName ?? "Linux",
            OsVersion = osVersion,
            KernelVersion = kernel.Length > 0 ? kernel : null,
            Architecture = AgentInfo.Architecture,
            CpuModel = cpu.Model,
            CpuCores = cpu.PhysicalCores,
            CpuLogicalProcessors = cpu.LogicalProcessors > 0 ? cpu.LogicalProcessors : Environment.ProcessorCount,
            MemoryTotalBytes = ProcParsers.ParseMeminfo(LinuxFiles.ReadOrEmpty(_paths.InProc("meminfo"))).TotalBytes,
            BootTime = uptime.Length > 0 ? BootTime(ProcParsers.ParseUptimeSeconds(uptime)) : null,
            IpAddresses = NetworkAddresses.Collect(),
        };
    }

    /// <summary>The machine's own name. A container has a name of its own, so its host's is read instead.</summary>
    private string Hostname()
    {
        var name = _paths.Containerized ? LinuxFiles.ReadOrEmpty($"{_paths.Etc}/hostname").Trim() : "";
        return name.Length > 0 ? name : Environment.MachineName;
    }

    public string? ReadMachineId() =>
        new[] { $"{_paths.Etc}/machine-id", $"{_paths.Root}/var/lib/dbus/machine-id" }
            .Select(path => LinuxFiles.ReadOrEmpty(path).Trim())
            .FirstOrDefault(id => id.Length > 0);

    /// <summary>Boot time to the minute, so it does not wobble between inventory reports.</summary>
    private static DateTimeOffset BootTime(double uptimeSeconds)
    {
        var boot = DateTimeOffset.UtcNow.AddSeconds(-uptimeSeconds);
        return new DateTimeOffset(boot.Ticks - boot.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }
}
