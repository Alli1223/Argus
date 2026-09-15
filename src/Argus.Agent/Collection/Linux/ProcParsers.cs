using System.Globalization;
using System.Text;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Linux;

/// <summary>Aggregate CPU time counters from /proc/stat, in clock ticks.</summary>
internal readonly record struct CpuTimes(
    ulong User, ulong Nice, ulong System, ulong Idle, ulong Iowait, ulong Irq, ulong SoftIrq, ulong Steal)
{
    // Guest time is already included in user time, so it is not added again.
    public ulong Total => User + Nice + System + Idle + Iowait + Irq + SoftIrq + Steal;

    /// <summary>CPU usage between two readings, as percentages of all CPU time.</summary>
    public static CpuMetrics Usage(CpuTimes previous, CpuTimes current)
    {
        var total = Counters.Delta(previous.Total, current.Total);
        if (total == 0)
        {
            return new CpuMetrics { UsagePercent = 0 };
        }

        double Percent(ulong ticks) => Math.Min(100, 100.0 * ticks / total);

        var idle = Counters.Delta(previous.Idle + previous.Iowait, current.Idle + current.Iowait);
        return new CpuMetrics
        {
            UsagePercent = Percent(total - Math.Min(idle, total)),
            UserPercent = Percent(Counters.Delta(previous.User + previous.Nice, current.User + current.Nice)),
            SystemPercent = Percent(Counters.Delta(
                previous.System + previous.Irq + previous.SoftIrq, current.System + current.Irq + current.SoftIrq)),
            IowaitPercent = Percent(Counters.Delta(previous.Iowait, current.Iowait)),
            StealPercent = Percent(Counters.Delta(previous.Steal, current.Steal)),
        };
    }
}

/// <summary>Cumulative IO counters of one block device from /proc/diskstats.</summary>
internal readonly record struct DiskCounters(
    string Name, ulong ReadsCompleted, ulong SectorsRead, ulong WritesCompleted, ulong SectorsWritten, ulong IoTimeMs);

/// <summary>Cumulative traffic counters of one network interface from /proc/net/dev.</summary>
internal readonly record struct InterfaceCounters(
    string Name, ulong RxBytes, ulong RxPackets, ulong RxErrors, ulong TxBytes, ulong TxPackets, ulong TxErrors);

internal sealed record MountEntry(string Device, string MountPoint, string FsType);

internal sealed record CpuInfo(string? Model, int? PhysicalCores, int LogicalProcessors);

/// <summary>
/// Parsers for the Linux /proc files the collectors read. They take file contents rather than paths
/// so they can be tested against captured files.
/// </summary>
internal static class ProcParsers
{
    private const long SectorBytes = 512;

    private static readonly HashSet<string> DiskFilesystems =
    [
        "ext2", "ext3", "ext4", "xfs", "btrfs", "bcachefs", "zfs", "f2fs", "jfs", "reiserfs",
        "vfat", "exfat", "ntfs", "ntfs3", "fuseblk",
    ];

    // Network filesystems are skipped on purpose: statvfs on an unreachable NFS/CIFS server can hang.
    private static readonly HashSet<string> SkippedFilesystems =
    [
        "tmpfs", "devtmpfs", "ramfs", "proc", "sysfs", "cgroup", "cgroup2", "overlay", "squashfs", "autofs",
        "debugfs", "tracefs", "securityfs", "pstore", "bpf", "mqueue", "hugetlbfs", "configfs", "fusectl",
        "binfmt_misc", "efivarfs", "nsfs", "rpc_pipefs", "devpts", "nfs", "nfs4", "cifs", "smb3", "smbfs",
        "fuse.gvfsd-fuse", "fuse.portal", "iso9660", "udf",
    ];

    private static readonly string[] SkippedMountPrefixes = ["/proc", "/sys", "/dev", "/run", "/snap", "/var/lib/docker", "/var/lib/containers"];

    public static CpuTimes ParseCpuTimes(string procStat)
    {
        foreach (var line in procStat.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                continue;
            }

            var f = Numbers(line, skip: 1);
            return new CpuTimes(f[0], f[1], f[2], f[3], At(f, 4), At(f, 5), At(f, 6), At(f, 7));
        }

        throw new FormatException("/proc/stat has no aggregate cpu line.");
    }

    public static MemoryMetrics ParseMeminfo(string meminfo)
    {
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in meminfo.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var parts = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                values[line[..colon]] = parts.Length > 1 && parts[1] == "kB" ? value * 1024 : value;
            }
        }

        long Get(string key) => values.GetValueOrDefault(key);

        var total = Get("MemTotal");

        // MemAvailable only exists since Linux 3.14; estimate it on older kernels.
        var available = values.TryGetValue("MemAvailable", out var reported)
            ? reported
            : Get("MemFree") + Get("Buffers") + Get("Cached");
        available = Math.Clamp(available, 0, total);

        var swapTotal = Get("SwapTotal");
        return new MemoryMetrics
        {
            TotalBytes = total,
            AvailableBytes = available,
            UsedBytes = total - available,
            CachedBytes = Get("Buffers") + Get("Cached") + Get("SReclaimable"),
            SwapTotalBytes = swapTotal,
            SwapUsedBytes = Math.Clamp(swapTotal - Get("SwapFree"), 0, swapTotal),
        };
    }

    public static LoadMetrics ParseLoadavg(string loadavg)
    {
        var parts = loadavg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new LoadMetrics { Load1 = Double(parts[0]), Load5 = Double(parts[1]), Load15 = Double(parts[2]) };
    }

    public static double ParseUptimeSeconds(string uptime) =>
        Double(uptime.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);

    public static List<DiskCounters> ParseDiskstats(string diskstats)
    {
        var disks = new List<DiskCounters>();
        foreach (var line in diskstats.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 14)
            {
                continue;
            }

            disks.Add(new DiskCounters(
                Name: parts[2],
                ReadsCompleted: ULong(parts[3]),
                SectorsRead: ULong(parts[5]),
                WritesCompleted: ULong(parts[7]),
                SectorsWritten: ULong(parts[9]),
                IoTimeMs: ULong(parts[12])));
        }

        return disks;
    }

    /// <summary>
    /// Aggregate IO of the given physical disks between two readings. Utilisation is that of the
    /// busiest disk, which is what matters when looking for a saturated device.
    /// </summary>
    public static DiskIoMetrics DiskRates(
        IEnumerable<DiskCounters> previous, IEnumerable<DiskCounters> current, IReadOnlySet<string> disks, double seconds)
    {
        var before = previous.Where(d => disks.Contains(d.Name)).ToDictionary(d => d.Name);
        double readBytes = 0, writeBytes = 0, reads = 0, writes = 0, busiest = 0;

        foreach (var now in current.Where(d => disks.Contains(d.Name)))
        {
            if (!before.TryGetValue(now.Name, out var then))
            {
                continue;
            }

            readBytes += Counters.Rate(then.SectorsRead, now.SectorsRead, seconds) * SectorBytes;
            writeBytes += Counters.Rate(then.SectorsWritten, now.SectorsWritten, seconds) * SectorBytes;
            reads += Counters.Rate(then.ReadsCompleted, now.ReadsCompleted, seconds);
            writes += Counters.Rate(then.WritesCompleted, now.WritesCompleted, seconds);
            busiest = Math.Max(busiest, Counters.Rate(then.IoTimeMs, now.IoTimeMs, seconds) / 10);
        }

        return new DiskIoMetrics
        {
            ReadBytesPerSec = readBytes,
            WriteBytesPerSec = writeBytes,
            ReadOpsPerSec = reads,
            WriteOpsPerSec = writes,
            UtilizationPercent = Math.Min(100, busiest),
        };
    }

    public static List<InterfaceCounters> ParseNetDev(string netDev)
    {
        var interfaces = new List<InterfaceCounters>();
        foreach (var line in netDev.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            // Old kernels print "eth0:123" without a space, so split on the colon first.
            var name = line[..colon].Trim();
            var f = Numbers(line[(colon + 1)..], skip: 0);
            if (name.Length == 0 || f.Length < 16)
            {
                continue;
            }

            interfaces.Add(new InterfaceCounters(name, f[0], f[1], f[2], f[8], f[9], f[10]));
        }

        return interfaces;
    }

    public static NetworkInterfaceMetrics InterfaceRates(InterfaceCounters previous, InterfaceCounters current, double seconds) => new()
    {
        Name = current.Name,
        RxBytesPerSec = Counters.Rate(previous.RxBytes, current.RxBytes, seconds),
        TxBytesPerSec = Counters.Rate(previous.TxBytes, current.TxBytes, seconds),
        RxPacketsPerSec = Counters.Rate(previous.RxPackets, current.RxPackets, seconds),
        TxPacketsPerSec = Counters.Rate(previous.TxPackets, current.TxPackets, seconds),
        RxErrorsPerSec = Counters.Rate(previous.RxErrors, current.RxErrors, seconds),
        TxErrorsPerSec = Counters.Rate(previous.TxErrors, current.TxErrors, seconds),
    };

    public static List<MountEntry> ParseMounts(string mounts)
    {
        var entries = new List<MountEntry>();
        foreach (var line in mounts.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                entries.Add(new MountEntry(Unescape(parts[0]), Unescape(parts[1]), parts[2]));
            }
        }

        return entries;
    }

    /// <summary>
    /// Picks the mounts worth reporting: real on-disk filesystems, no pseudo, network, snap or container
    /// mounts, and each device once (btrfs subvolumes and bind mounts all show the same usage).
    /// </summary>
    public static List<MountEntry> SelectFilesystems(IEnumerable<MountEntry> mounts) =>
        mounts
            .Where(m => !SkippedFilesystems.Contains(m.FsType))
            .Where(m => DiskFilesystems.Contains(m.FsType) || m.Device.StartsWith("/dev/", StringComparison.Ordinal))
            .Where(m => !m.Device.StartsWith("/dev/loop", StringComparison.Ordinal))
            .Where(m => m.MountPoint == "/" || !SkippedMountPrefixes.Any(prefix =>
                m.MountPoint == prefix || m.MountPoint.StartsWith(prefix + "/", StringComparison.Ordinal)))
            .GroupBy(m => m.Device)
            .Select(group => group.OrderBy(m => m.MountPoint.Length).ThenBy(m => m.MountPoint, StringComparer.Ordinal).First())
            .OrderBy(m => m.MountPoint, StringComparer.Ordinal)
            .ToList();

    public static (string? PrettyName, string? VersionId) ParseOsRelease(string osRelease)
    {
        string? prettyName = null, versionId = null, name = null;
        foreach (var line in osRelease.Split('\n'))
        {
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var value = line[(equals + 1)..].Trim().Trim('"', '\'');
            switch (line[..equals].Trim())
            {
                case "PRETTY_NAME": prettyName = value; break;
                case "VERSION_ID": versionId = value; break;
                case "NAME": name = value; break;
            }
        }

        return (prettyName ?? name, versionId);
    }

    public static CpuInfo ParseCpuInfo(string cpuinfo)
    {
        string? model = null, armModel = null, hardware = null;
        var logical = 0;
        var cores = new HashSet<(string, string)>();
        string? physicalId = null;

        foreach (var line in cpuinfo.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "processor": logical++; break;
                case "model name": model ??= value; break;
                case "Model": armModel ??= value; break;
                case "Hardware": hardware ??= value; break;
                case "physical id": physicalId = value; break;
                case "core id": cores.Add((physicalId ?? "0", value)); break;
            }
        }

        return new CpuInfo(model ?? armModel ?? hardware, cores.Count > 0 ? cores.Count : null, logical);
    }

    private static ulong[] Numbers(string line, int skip) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(skip).Select(ULong).ToArray();

    private static ulong At(ulong[] values, int index) => index < values.Length ? values[index] : 0;

    private static ulong ULong(string value) =>
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static double Double(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>/proc/mounts escapes spaces, tabs, newlines and backslashes as octal (\040 …).</summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 3 < value.Length
                && IsOctal(value[i + 1]) && IsOctal(value[i + 2]) && IsOctal(value[i + 3]))
            {
                result.Append((char)Convert.ToInt32(value.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                result.Append(value[i]);
            }
        }

        return result.ToString();
    }

    private static bool IsOctal(char c) => c is >= '0' and <= '7';
}
