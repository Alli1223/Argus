using Argus.Agent.Collection.Containers;
using Argus.Agent.Collection.Linux;
using Argus.Agent.Collection.Windows;
using Argus.Agent.Configuration;

namespace Argus.Agent.Collection;

internal static class PlatformCollectors
{
    public static ISystemMetricsSource CreateMetricsSource(ILoggerFactory loggers, AgentConfig config)
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxMetricsSource(loggers.CreateLogger<LinuxMetricsSource>(), Paths(config));
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsMetricsSource(loggers.CreateLogger<WindowsMetricsSource>());
        }

        throw new PlatformNotSupportedException("The Argus agent runs on Linux and Windows.");
    }

    public static ISystemInfoSource CreateSystemInfoSource(AgentConfig config)
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxSystemInfoSource(Paths(config));
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsSystemInfoSource();
        }

        throw new PlatformNotSupportedException("The Argus agent runs on Linux and Windows.");
    }

    /// <summary>Service checks where the system allows them; Linux without systemd has none.</summary>
    public static IServiceStatusSource CreateServiceStatusSource(ILoggerFactory loggers)
    {
        if (OperatingSystem.IsLinux() && SystemdServices.IsAvailable)
        {
            return new SystemdServices(loggers.CreateLogger<SystemdServices>());
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsServices(loggers.CreateLogger<WindowsServices>());
        }

        return new NoServiceStatus();
    }

    /// <summary>Docker's containers on Linux. (Docker on Windows is not watched yet.)</summary>
    public static IContainerSource CreateContainerSource(ILoggerFactory loggers, AgentConfig config, TimeProvider time) =>
        OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(config.DockerSocket)
            ? new DockerContainers(config.DockerSocket, config.ContainerActions, time, loggers.CreateLogger<DockerContainers>())
            : new NoContainers();

    /// <summary>Where this agent finds the machine's files: under a root of its own when containerized.</summary>
    private static HostPaths Paths(AgentConfig config) => new(config.HostRoot);

    public static ITemperatureSource CreateTemperatureSource(ILoggerFactory loggers, AgentConfig config)
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxTemperatures(config.DriveTemperatures, Paths(config).Sys);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsTemperatures(loggers.CreateLogger<WindowsTemperatures>());
        }

        return new NoTemperatures();
    }
}
