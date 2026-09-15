using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Argus.Contracts.Agent;
using Microsoft.Win32;

namespace Argus.Agent.Collection.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSystemInfoSource : ISystemInfoSource
{
    private const int FirstWindows11Build = 22000;

    public SystemInfo Collect()
    {
        using var currentVersion = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var productName = currentVersion?.GetValue("ProductName") as string;
        var displayVersion = currentVersion?.GetValue("DisplayVersion") as string;
        var build = currentVersion?.GetValue("CurrentBuildNumber") as string;
        var revision = currentVersion?.GetValue("UBR") as int?;

        // Windows 11 still says "Windows 10" in ProductName; the build number tells them apart.
        if (productName is not null && productName.StartsWith("Windows 10", StringComparison.Ordinal)
            && int.TryParse(build, out var buildNumber) && buildNumber >= FirstWindows11Build)
        {
            productName = "Windows 11" + productName["Windows 10".Length..];
        }

        using var processor = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var memory = WindowsNative.GetMemoryStatus();

        return new SystemInfo
        {
            Hostname = Environment.MachineName,
            Platform = HostPlatform.Windows,
            OsName = productName ?? RuntimeInformation.OSDescription,
            OsVersion = displayVersion ?? Environment.OSVersion.Version.ToString(),
            KernelVersion = build is null ? Environment.OSVersion.Version.ToString()
                : revision is null ? $"10.0.{build}"
                : $"10.0.{build}.{revision}",
            Architecture = AgentInfo.Architecture,
            CpuModel = (processor?.GetValue("ProcessorNameString") as string)?.Trim(),
            CpuCores = WindowsNative.CountPhysicalCores(),
            CpuLogicalProcessors = Environment.ProcessorCount,
            MemoryTotalBytes = (long)memory.TotalPhys,
            BootTime = BootTime(),
            IpAddresses = NetworkAddresses.Collect(),
        };
    }

    public string? ReadMachineId()
    {
        using var cryptography = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return cryptography?.GetValue("MachineGuid") as string;
    }

    /// <summary>Boot time to the minute, so it does not wobble between inventory reports.</summary>
    private static DateTimeOffset BootTime()
    {
        var boot = DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
        return new DateTimeOffset(boot.Ticks - boot.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }
}
