using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Windows;

/// <summary>Reads CPU, memory, disk, filesystem and network metrics through Win32, PDH and .NET APIs.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMetricsSource(ILogger<WindowsMetricsSource> logger) : ISystemMetricsSource, IDisposable
{
    private const string DiskReadBytes = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    private const string DiskWriteBytes = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";
    private const string DiskReads = @"\PhysicalDisk(_Total)\Disk Reads/sec";
    private const string DiskWrites = @"\PhysicalDisk(_Total)\Disk Writes/sec";
    private const string DiskIdleTime = @"\PhysicalDisk(_Total)\% Idle Time";

    private PdhQuery? _pdh;
    private (ulong Idle, ulong Kernel, ulong User)? _cpu;
    private Dictionary<string, InterfaceSnapshot>? _interfaces;
    private long _lastReading;

    private readonly record struct InterfaceSnapshot(
        long RxBytes, long TxBytes, long RxPackets, long TxPackets, long RxErrors, long TxErrors);

    public void Prime()
    {
        try
        {
            _pdh ??= new PdhQuery([DiskReadBytes, DiskWriteBytes, DiskReads, DiskWrites, DiskIdleTime]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            logger.LogWarning("Disk performance counters are unavailable: {Reason}", ex.Message);
        }

        Collect();
    }

    public SystemReading Collect()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastReading == 0 ? 0 : Stopwatch.GetElapsedTime(_lastReading, now).TotalSeconds;
        _lastReading = now;

        DiskIoMetrics? diskIo = null;
        if (_pdh is not null && seconds > 0)
        {
            _pdh.Collect();
            var idle = _pdh.Read(DiskIdleTime);
            diskIo = new DiskIoMetrics
            {
                ReadBytesPerSec = _pdh.Read(DiskReadBytes) ?? 0,
                WriteBytesPerSec = _pdh.Read(DiskWriteBytes) ?? 0,
                ReadOpsPerSec = _pdh.Read(DiskReads) ?? 0,
                WriteOpsPerSec = _pdh.Read(DiskWrites) ?? 0,
                UtilizationPercent = idle is { } idlePercent ? Math.Clamp(100 - idlePercent, 0, 100) : null,
            };
        }

        var (network, interfaces) = ReadNetwork(seconds);

        return new SystemReading(
            ReadCpu(),
            ReadMemory(),
            Load: null,
            diskIo,
            network,
            Environment.TickCount64 / 1000,
            ReadFilesystems(),
            interfaces);
    }

    public void Dispose() => _pdh?.Dispose();

    private CpuMetrics ReadCpu()
    {
        if (!WindowsNative.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return new CpuMetrics { UsagePercent = 0 };
        }

        var current = (Idle: idleTime.Ticks, Kernel: kernelTime.Ticks, User: userTime.Ticks);
        var previous = _cpu;
        _cpu = current;
        if (previous is not { } before)
        {
            return new CpuMetrics { UsagePercent = 0 };
        }

        // Kernel time includes idle time.
        var idle = Counters.Delta(before.Idle, current.Idle);
        var kernel = Counters.Delta(before.Kernel, current.Kernel);
        var user = Counters.Delta(before.User, current.User);
        var total = kernel + user;
        if (total == 0)
        {
            return new CpuMetrics { UsagePercent = 0 };
        }

        idle = Math.Min(idle, kernel);
        return new CpuMetrics
        {
            UsagePercent = 100.0 * (total - idle) / total,
            UserPercent = 100.0 * user / total,
            SystemPercent = 100.0 * (kernel - idle) / total,
        };
    }

    private static MemoryMetrics ReadMemory()
    {
        var status = WindowsNative.GetMemoryStatus();
        var total = (long)status.TotalPhys;
        var available = (long)status.AvailPhys;

        // Windows reports a commit limit (RAM + page files) rather than swap; derive page-file figures from it.
        var swapTotal = Math.Max(0, (long)status.TotalPageFile - total);
        var committed = (long)(status.TotalPageFile - status.AvailPageFile);
        var swapUsed = Math.Clamp(committed - (total - available), 0, swapTotal);

        return new MemoryMetrics
        {
            TotalBytes = total,
            AvailableBytes = available,
            UsedBytes = total - available,
            SwapTotalBytes = swapTotal,
            SwapUsedBytes = swapUsed,
        };
    }

    private List<FilesystemMetrics> ReadFilesystems()
    {
        var filesystems = new List<FilesystemMetrics>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                filesystems.Add(new FilesystemMetrics
                {
                    MountPoint = drive.Name,
                    Device = drive.VolumeLabel is { Length: > 0 } label ? label : null,
                    FsType = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    UsedBytes = drive.TotalSize - drive.TotalFreeSpace,
                    AvailableBytes = drive.AvailableFreeSpace,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug("Skipping drive {Drive}: {Reason}", drive.Name, ex.Message);
            }
        }

        return filesystems;
    }

    private (NetworkMetrics? Total, List<NetworkInterfaceMetrics> Interfaces) ReadNetwork(double seconds)
    {
        var current = new Dictionary<string, InterfaceSnapshot>(StringComparer.Ordinal);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var stats = nic.GetIPStatistics();
            current.TryAdd(nic.Name, new InterfaceSnapshot(
                stats.BytesReceived,
                stats.BytesSent,
                stats.UnicastPacketsReceived + stats.NonUnicastPacketsReceived,
                stats.UnicastPacketsSent + stats.NonUnicastPacketsSent,
                stats.IncomingPacketsWithErrors,
                stats.OutgoingPacketsWithErrors));
        }

        var previous = _interfaces;
        _interfaces = current;
        if (previous is null || seconds <= 0)
        {
            return (null, []);
        }

        var interfaces = new List<NetworkInterfaceMetrics>();
        foreach (var (name, now) in current)
        {
            if (!previous.TryGetValue(name, out var then))
            {
                continue;
            }

            interfaces.Add(new NetworkInterfaceMetrics
            {
                Name = name,
                RxBytesPerSec = Rate(then.RxBytes, now.RxBytes, seconds),
                TxBytesPerSec = Rate(then.TxBytes, now.TxBytes, seconds),
                RxPacketsPerSec = Rate(then.RxPackets, now.RxPackets, seconds),
                TxPacketsPerSec = Rate(then.TxPackets, now.TxPackets, seconds),
                RxErrorsPerSec = Rate(then.RxErrors, now.RxErrors, seconds),
                TxErrorsPerSec = Rate(then.TxErrors, now.TxErrors, seconds),
            });
        }

        var total = new NetworkMetrics
        {
            RxBytesPerSec = interfaces.Sum(i => i.RxBytesPerSec),
            TxBytesPerSec = interfaces.Sum(i => i.TxBytesPerSec),
        };
        return (total, interfaces);
    }

    private static double Rate(long previous, long current, double seconds) =>
        Counters.Rate((ulong)Math.Max(0, previous), (ulong)Math.Max(0, current), seconds);
}
