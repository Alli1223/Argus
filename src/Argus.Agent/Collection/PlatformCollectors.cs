using Argus.Agent.Collection.Linux;
using Argus.Agent.Collection.Windows;

namespace Argus.Agent.Collection;

internal static class PlatformCollectors
{
    public static ISystemMetricsSource CreateMetricsSource(ILoggerFactory loggers)
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxMetricsSource(loggers.CreateLogger<LinuxMetricsSource>());
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsMetricsSource(loggers.CreateLogger<WindowsMetricsSource>());
        }

        throw new PlatformNotSupportedException("The Argus agent runs on Linux and Windows.");
    }

    public static ISystemInfoSource CreateSystemInfoSource()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxSystemInfoSource();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsSystemInfoSource();
        }

        throw new PlatformNotSupportedException("The Argus agent runs on Linux and Windows.");
    }
}
