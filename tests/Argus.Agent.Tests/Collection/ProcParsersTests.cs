using Argus.Agent.Collection;
using Argus.Agent.Collection.Linux;

namespace Argus.Agent.Tests.Collection;

public class ProcParsersTests
{
    [Fact]
    public void Cpu_usage_is_computed_from_two_proc_stat_readings()
    {
        var first = ProcParsers.ParseCpuTimes("""
            cpu  1000 0 500 8000 100 0 0 0 0 0
            cpu0 500 0 250 4000 50 0 0 0 0 0
            intr 12345
            """);
        var second = ProcParsers.ParseCpuTimes("cpu  1600 0 700 8900 200 50 50 100 0 0\n");

        var usage = CpuTimes.Usage(first, second);

        // 2000 ticks elapsed; idle+iowait grew by 1000, so half the time was busy.
        Assert.Equal(50, usage.UsagePercent, precision: 6);
        Assert.Equal(30, usage.UserPercent!.Value, precision: 6);
        Assert.Equal(15, usage.SystemPercent!.Value, precision: 6);
        Assert.Equal(5, usage.IowaitPercent!.Value, precision: 6);
        Assert.Equal(5, usage.StealPercent!.Value, precision: 6);
    }

    [Fact]
    public void Counter_resets_count_as_no_change()
    {
        Assert.Equal(0UL, Counters.Delta(100, 50));
        Assert.Equal(0, Counters.Rate(100, 50, 10));
        Assert.Equal(5, Counters.Rate(100, 150, 10));

        var usage = CpuTimes.Usage(new CpuTimes(100, 0, 100, 1000, 10, 0, 0, 0), new CpuTimes(100, 0, 100, 1000, 10, 0, 0, 0));
        Assert.Equal(0, usage.UsagePercent);
    }

    [Fact]
    public void Meminfo_reports_used_as_total_minus_available()
    {
        var memory = ProcParsers.ParseMeminfo("""
            MemTotal:       16000000 kB
            MemFree:         2000000 kB
            MemAvailable:   10000000 kB
            Buffers:          500000 kB
            Cached:          6000000 kB
            SwapCached:            0 kB
            SReclaimable:     300000 kB
            SwapTotal:       4000000 kB
            SwapFree:        3000000 kB
            HugePages_Total:       0
            """);

        Assert.Equal(16_000_000L * 1024, memory.TotalBytes);
        Assert.Equal(10_000_000L * 1024, memory.AvailableBytes);
        Assert.Equal(6_000_000L * 1024, memory.UsedBytes);
        Assert.Equal(6_800_000L * 1024, memory.CachedBytes);
        Assert.Equal(4_000_000L * 1024, memory.SwapTotalBytes);
        Assert.Equal(1_000_000L * 1024, memory.SwapUsedBytes);
    }

    [Fact]
    public void Meminfo_estimates_available_memory_on_old_kernels()
    {
        var memory = ProcParsers.ParseMeminfo("MemTotal: 1000 kB\nMemFree: 200 kB\nBuffers: 100 kB\nCached: 300 kB\n");

        Assert.Equal(600L * 1024, memory.AvailableBytes);
        Assert.Equal(400L * 1024, memory.UsedBytes);
    }

    [Fact]
    public void Loadavg_and_uptime_are_parsed()
    {
        var load = ProcParsers.ParseLoadavg("0.52 1.58 2.59 1/345 12345\n");

        Assert.Equal(0.52, load.Load1);
        Assert.Equal(1.58, load.Load5);
        Assert.Equal(2.59, load.Load15);
        Assert.Equal(12345.67, ProcParsers.ParseUptimeSeconds("12345.67 45678.90\n"));
    }

    [Fact]
    public void Disk_rates_cover_only_physical_disks()
    {
        var before = ProcParsers.ParseDiskstats("""
             259       0 nvme0n1 1000 0 20000 0 500 0 40000 0 0 1000 0 0 0 0 0
             259       1 nvme0n1p1 900 0 18000 0 400 0 30000 0 0 900 0 0 0 0 0
               7       0 loop0 10 0 100 0 0 0 0 0 0 10 0 0 0 0 0
               8       0 sda 100 0 1000 0 50 0 2000 0 0 100 0 0 0 0 0
            """);
        var after = ProcParsers.ParseDiskstats("""
             259       0 nvme0n1 1100 0 40000 0 600 0 80000 0 0 6000 0 0 0 0 0
             259       1 nvme0n1p1 1000 0 38000 0 500 0 70000 0 0 5900 0 0 0 0 0
               7       0 loop0 20 0 9999 0 0 0 0 0 0 20 0 0 0 0 0
               8       0 sda 110 0 3000 0 60 0 4000 0 0 1100 0 0 0 0 0
            """);

        var rates = ProcParsers.DiskRates(before, after, new HashSet<string> { "nvme0n1", "sda" }, seconds: 10);

        Assert.Equal((20000 + 2000) * 512 / 10.0, rates.ReadBytesPerSec);
        Assert.Equal((40000 + 2000) * 512 / 10.0, rates.WriteBytesPerSec);
        Assert.Equal((100 + 10) / 10.0, rates.ReadOpsPerSec);
        Assert.Equal((100 + 10) / 10.0, rates.WriteOpsPerSec);
        Assert.Equal(50, rates.UtilizationPercent);
    }

    [Fact]
    public void Net_dev_counters_become_per_second_rates()
    {
        var before = ProcParsers.ParseNetDev("""
            Inter-|   Receive                                                |  Transmit
             face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
                lo: 5000 50 0 0 0 0 0 0 5000 50 0 0 0 0 0 0
              eth0:10000 100 1 0 0 0 0 0 20000 200 0 0 0 0 0 0
            """);
        var after = ProcParsers.ParseNetDev("""
                lo: 6000 60 0 0 0 0 0 0 6000 60 0 0 0 0 0 0
              eth0: 30000 300 3 0 0 0 0 0 60000 400 2 0 0 0 0 0
            """);

        Assert.Equal(["lo", "eth0"], before.Select(i => i.Name));
        var eth0 = ProcParsers.InterfaceRates(before[1], after[1], seconds: 10);

        Assert.Equal("eth0", eth0.Name);
        Assert.Equal(2000, eth0.RxBytesPerSec);
        Assert.Equal(4000, eth0.TxBytesPerSec);
        Assert.Equal(20, eth0.RxPacketsPerSec);
        Assert.Equal(20, eth0.TxPacketsPerSec);
        Assert.Equal(0.2, eth0.RxErrorsPerSec, precision: 6);
        Assert.Equal(0.2, eth0.TxErrorsPerSec, precision: 6);
    }

    [Fact]
    public void Only_real_filesystems_are_selected_once_per_device()
    {
        var mounts = ProcParsers.ParseMounts("""
            proc /proc proc rw,nosuid,nodev,noexec,relatime 0 0
            sysfs /sys sysfs rw,nosuid,nodev,noexec,relatime 0 0
            /dev/nvme0n1p2 / btrfs rw,noatime,subvol=/@ 0 0
            /dev/nvme0n1p2 /home btrfs rw,noatime,subvol=/@home 0 0
            /dev/nvme0n1p1 /boot/efi vfat rw,relatime 0 0
            tmpfs /tmp tmpfs rw,nosuid,nodev 0 0
            /dev/loop3 /snap/core/123 squashfs ro,nodev,relatime 0 0
            overlay /var/lib/docker/overlay2/abc/merged overlay rw 0 0
            server:/export /mnt/nfs nfs4 rw 0 0
            /dev/sdb1 /mnt/My\040Disk ext4 rw 0 0
            """);

        var selected = ProcParsers.SelectFilesystems(mounts);

        Assert.Equal(["/", "/boot/efi", "/mnt/My Disk"], selected.Select(m => m.MountPoint));
        Assert.Equal("btrfs", selected[0].FsType);
    }

    [Fact]
    public void Os_release_prefers_the_pretty_name()
    {
        var (name, version) = ProcParsers.ParseOsRelease("""
            NAME="Ubuntu"
            VERSION_ID="24.04"
            PRETTY_NAME="Ubuntu 24.04.1 LTS"
            ID=ubuntu
            """);

        Assert.Equal("Ubuntu 24.04.1 LTS", name);
        Assert.Equal("24.04", version);
        Assert.Equal("Arch Linux", ProcParsers.ParseOsRelease("NAME=\"Arch Linux\"\nID=arch\n").PrettyName);
    }

    [Fact]
    public void Cpuinfo_counts_physical_cores_and_logical_processors()
    {
        var info = ProcParsers.ParseCpuInfo("""
            processor	: 0
            model name	: AMD Ryzen 9 7950X 16-Core Processor
            physical id	: 0
            core id		: 0

            processor	: 1
            model name	: AMD Ryzen 9 7950X 16-Core Processor
            physical id	: 0
            core id		: 0

            processor	: 2
            model name	: AMD Ryzen 9 7950X 16-Core Processor
            physical id	: 0
            core id		: 1
            """);

        Assert.Equal("AMD Ryzen 9 7950X 16-Core Processor", info.Model);
        Assert.Equal(2, info.PhysicalCores);
        Assert.Equal(3, info.LogicalProcessors);
    }

    [Fact]
    public void Cpuinfo_on_arm_falls_back_to_the_board_model()
    {
        var info = ProcParsers.ParseCpuInfo("processor\t: 0\nBogoMIPS\t: 108.00\nprocessor\t: 1\nModel\t\t: Raspberry Pi 5 Model B Rev 1.0\n");

        Assert.Equal("Raspberry Pi 5 Model B Rev 1.0", info.Model);
        Assert.Null(info.PhysicalCores);
        Assert.Equal(2, info.LogicalProcessors);
    }

    [Fact]
    public void Machine_ids_are_hashed()
    {
        var hashed = MachineIdentity.Hash("0123456789abcdef0123456789abcdef\n");

        Assert.Equal(64, hashed.Length);
        Assert.Equal(hashed, MachineIdentity.Hash("0123456789abcdef0123456789abcdef"));
        Assert.DoesNotContain("0123456789abcdef", hashed, StringComparison.Ordinal);
    }
}
