namespace Argus.Server.Features.Alerts;

/// <summary>What an alert rule watches.</summary>
public enum AlertMetric
{
    /// <summary>CPU usage, percent of all cores.</summary>
    CpuUsage,

    /// <summary>Memory in use, percent of physical memory.</summary>
    MemoryUsage,

    /// <summary>Swap / page file in use, percent.</summary>
    SwapUsage,

    /// <summary>1-minute load average divided by the number of logical processors.</summary>
    LoadPerCore,

    /// <summary>Busiest disk's utilisation, percent.</summary>
    DiskIoUtilization,

    /// <summary>Received network traffic, bytes per second.</summary>
    NetworkReceive,

    /// <summary>Transmitted network traffic, bytes per second.</summary>
    NetworkTransmit,

    /// <summary>Filesystem space used, percent; evaluated per mount point.</summary>
    DiskUsage,

    /// <summary>Filesystem inodes used, percent; evaluated per mount point.</summary>
    InodeUsage,

    /// <summary>The host has not reported for the rule's duration.</summary>
    HostOffline,
}

public enum AlertOperator
{
    Above,
    Below,
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical,
}

public enum AlertStatus
{
    Firing,
    Resolved,
}

public static class AlertMetrics
{
    /// <summary>Metrics evaluated once per filesystem rather than once per host.</summary>
    public static bool IsPerFilesystem(this AlertMetric metric) => metric is AlertMetric.DiskUsage or AlertMetric.InodeUsage;

    public static bool IsPercentage(this AlertMetric metric) =>
        metric is AlertMetric.CpuUsage or AlertMetric.MemoryUsage or AlertMetric.SwapUsage
            or AlertMetric.DiskIoUtilization or AlertMetric.DiskUsage or AlertMetric.InodeUsage;

    public static string Label(this AlertMetric metric) => metric switch
    {
        AlertMetric.CpuUsage => "CPU usage",
        AlertMetric.MemoryUsage => "Memory usage",
        AlertMetric.SwapUsage => "Swap usage",
        AlertMetric.LoadPerCore => "Load per core",
        AlertMetric.DiskIoUtilization => "Disk utilisation",
        AlertMetric.NetworkReceive => "Network receive",
        AlertMetric.NetworkTransmit => "Network transmit",
        AlertMetric.DiskUsage => "Disk usage",
        AlertMetric.InodeUsage => "Inode usage",
        _ => "Host offline",
    };
}
