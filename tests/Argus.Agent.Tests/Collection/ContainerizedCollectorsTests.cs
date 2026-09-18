using System.Runtime.Versioning;
using Argus.Agent.Collection.Linux;
using Microsoft.Extensions.Logging.Abstractions;

namespace Argus.Agent.Tests.Collection;

/// <summary>
/// The agent in a container reads the machine through the root its filesystem is mounted at, not its
/// own. The machine here is a directory laid out like a NAS: its own /etc, /proc and /sys.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ContainerizedCollectorsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "argus-agent-tests", Guid.NewGuid().ToString("N"));

    public ContainerizedCollectorsTests()
    {
        Write("etc/hostname", "Alli-NAS\n");
        Write("etc/machine-id", "3f1e9c0a5b7d4e2f8a6c1b3d5e7f9a1c\n");
        Write("etc/os-release", "NAME=\"TOS\"\nVERSION_ID=\"6.0\"\n");
        Write("proc/sys/kernel/osrelease", "5.15.0-terramaster\n");
        Write("proc/cpuinfo", "processor\t: 0\nmodel name\t: Intel(R) Celeron(R) N5105\nphysical id\t: 0\ncore id\t: 0\n");
        Write("proc/meminfo", "MemTotal:        8123456 kB\nMemAvailable:    6123456 kB\nCached:          1000000 kB\n");
        Write("proc/stat", "cpu  1000 10 500 20000 100 0 20 0 0 0\ncpu0 500 5 250 10000 50 0 10 0 0 0\n");
        Write("proc/loadavg", "0.42 0.35 0.30 1/321 9876\n");
        Write("proc/uptime", "123456.78 987654.32\n");
        Write("proc/diskstats", "   8       0 sda 100 0 8000 50 200 0 16000 80 0 120 130 0 0 0 0 0 0\n");

        // The container's own mount table would list the image's layers, so the machine's first process answers.
        Write("proc/1/mounts", $"/dev/sda1 / ext4 rw,relatime 0 0\nproc /proc proc rw,nosuid 0 0\ntmpfs /run tmpfs rw 0 0\n");
        Write("proc/1/net/dev", """
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo:  1000      10    0    0    0     0          0         0     1000      10    0    0    0     0       0          0
              eth0: 500000     400    0    0    0     0          0         0   250000     200    0    0    0     0       0          0
            """);
        Write("sys/class/net/eth0/operstate", "up\n");
        Directory.CreateDirectory(Path.Combine(_root, "sys/class/net/eth0/device"));
        Directory.CreateDirectory(Path.Combine(_root, "sys/block/sda/device"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void The_machine_is_described_rather_than_the_container()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");

        var info = new LinuxSystemInfoSource(new HostPaths(_root)).Collect();

        Assert.Equal("Alli-NAS", info.Hostname);
        Assert.Equal("TOS", info.OsName);
        Assert.Equal("6.0", info.OsVersion);
        Assert.Equal("5.15.0-terramaster", info.KernelVersion);
        Assert.Equal("Intel(R) Celeron(R) N5105", info.CpuModel);
        Assert.Equal(8123456L * 1024, info.MemoryTotalBytes);
        Assert.Equal("3f1e9c0a5b7d4e2f8a6c1b3d5e7f9a1c", new LinuxSystemInfoSource(new HostPaths(_root)).ReadMachineId());
    }

    [Fact]
    public void The_machine_s_own_readings_are_taken()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux only");
        var source = new LinuxMetricsSource(NullLogger<LinuxMetricsSource>.Instance, new HostPaths(_root));
        source.Prime();

        // Counters that have not moved between readings, so only what they describe is interesting here.
        var reading = source.Collect();

        Assert.Equal(8123456L * 1024, reading.Memory.TotalBytes);
        Assert.Equal(0.42, reading.Load!.Load1);
        Assert.Equal(123456, reading.UptimeSeconds);
        Assert.Equal(["eth0"], reading.Interfaces.Select(nic => nic.Name));

        // The machine's root filesystem is measured where the container has it mounted.
        var root = Assert.Single(reading.Filesystems);
        Assert.Equal(("/", "/dev/sda1"), (root.MountPoint, root.Device));
        Assert.True(root.TotalBytes > 0);
    }
}
