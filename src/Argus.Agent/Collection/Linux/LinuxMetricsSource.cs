using System.Diagnostics;
using System.Runtime.Versioning;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Linux;

/// <summary>Reads CPU, memory, load, disk, filesystem and network metrics from /proc and /sys.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxMetricsSource(ILogger<LinuxMetricsSource> logger, HostPaths? paths = null) : ISystemMetricsSource
{
    private readonly HostPaths _paths = paths ?? HostPaths.Local;

    private CpuTimes? _cpu;
    private List<DiskCounters>? _disks;
    private List<InterfaceCounters>? _interfaces;
    private long _lastReading;

    public void Prime() => Collect();

    public SystemReading Collect()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastReading == 0 ? 0 : Stopwatch.GetElapsedTime(_lastReading, now).TotalSeconds;
        _lastReading = now;

        var cpuNow = ProcParsers.ParseCpuTimes(File.ReadAllText(_paths.InProc("stat")));
        var cpu = _cpu is { } cpuBefore ? CpuTimes.Usage(cpuBefore, cpuNow) : new CpuMetrics { UsagePercent = 0 };
        _cpu = cpuNow;

        var disksNow = ProcParsers.ParseDiskstats(LinuxFiles.ReadOrEmpty(_paths.InProc("diskstats")));
        DiskIoMetrics? diskIo = null;
        if (_disks is not null && seconds > 0)
        {
            diskIo = ProcParsers.DiskRates(_disks, disksNow, LinuxFiles.WithDevice($"{_paths.Sys}/block"), seconds);
        }

        _disks = disksNow;

        var interfacesNow = ProcParsers.ParseNetDev(LinuxFiles.ReadOrEmpty(_paths.NetDev));
        NetworkMetrics? network = null;
        List<NetworkInterfaceMetrics> interfaces = [];
        if (_interfaces is not null && seconds > 0)
        {
            (network, interfaces) = NetworkRates(_interfaces, interfacesNow, seconds);
        }

        _interfaces = interfacesNow;

        return new SystemReading(
            cpu,
            ProcParsers.ParseMeminfo(File.ReadAllText(_paths.InProc("meminfo"))),
            ProcParsers.ParseLoadavg(File.ReadAllText(_paths.InProc("loadavg"))),
            diskIo,
            network,
            (long)ProcParsers.ParseUptimeSeconds(File.ReadAllText(_paths.InProc("uptime"))),
            ReadFilesystems(),
            interfaces);
    }

    private (NetworkMetrics Total, List<NetworkInterfaceMetrics> Interfaces) NetworkRates(
        List<InterfaceCounters> previous, List<InterfaceCounters> current, double seconds)
    {
        var before = new Dictionary<string, InterfaceCounters>(StringComparer.Ordinal);
        foreach (var counters in previous)
        {
            before.TryAdd(counters.Name, counters);
        }

        var physical = LinuxFiles.WithDevice($"{_paths.Sys}/class/net");
        var interfaces = new List<NetworkInterfaceMetrics>();
        double physicalRx = 0, physicalTx = 0;
        var sawPhysical = false;

        foreach (var nic in current)
        {
            if (nic.Name == "lo" || NetworkAddresses.IsContainerInterface(nic.Name) || !IsUp(nic.Name)
                || !before.TryGetValue(nic.Name, out var then))
            {
                continue;
            }

            var rates = ProcParsers.InterfaceRates(then, nic, seconds);
            interfaces.Add(rates);

            // Physical NICs carry all traffic that leaves the machine; bridges and tunnels would count it twice.
            if (physical.Contains(nic.Name))
            {
                physicalRx += rates.RxBytesPerSec;
                physicalTx += rates.TxBytesPerSec;
                sawPhysical = true;
            }
        }

        var total = sawPhysical
            ? new NetworkMetrics { RxBytesPerSec = physicalRx, TxBytesPerSec = physicalTx }
            : new NetworkMetrics { RxBytesPerSec = interfaces.Sum(i => i.RxBytesPerSec), TxBytesPerSec = interfaces.Sum(i => i.TxBytesPerSec) };

        return (total, interfaces);
    }

    private bool IsUp(string interfaceName) =>
        LinuxFiles.ReadOrEmpty($"{_paths.Sys}/class/net/{interfaceName}/operstate").Trim() is "up" or "unknown";

    private List<FilesystemMetrics> ReadFilesystems()
    {
        var mounts = ProcParsers.SelectFilesystems(ProcParsers.ParseMounts(LinuxFiles.ReadOrEmpty(_paths.Mounts)));
        var filesystems = new List<FilesystemMetrics>(mounts.Count);

        foreach (var mount in mounts)
        {
            try
            {
                var drive = new DriveInfo(_paths.Mounted(mount.MountPoint));
                var total = drive.TotalSize;
                if (total <= 0)
                {
                    continue;
                }

                var (inodesTotal, inodesUsed) = Statvfs.TryGetInodes(_paths.Mounted(mount.MountPoint));
                filesystems.Add(new FilesystemMetrics
                {
                    MountPoint = mount.MountPoint,
                    Device = mount.Device,
                    FsType = mount.FsType,
                    TotalBytes = total,
                    UsedBytes = total - drive.TotalFreeSpace,
                    AvailableBytes = drive.AvailableFreeSpace,
                    InodesTotal = inodesTotal,
                    InodesUsed = inodesUsed,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogDebug("Skipping filesystem {MountPoint}: {Reason}", mount.MountPoint, ex.Message);
            }
        }

        return filesystems;
    }
}
