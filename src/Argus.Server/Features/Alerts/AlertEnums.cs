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

    /// <summary>A service that should be running has failed; evaluated per service.</summary>
    ServiceFailed,

    /// <summary>A Docker container is down: crashed, unhealthy, restarting or dead; evaluated per container.</summary>
    ContainerDown,

    /// <summary>Docker restarted a container more times than the threshold within the rule's duration.</summary>
    ContainerRestarts,

    /// <summary>A UPS is running on its battery because line power is gone; evaluated per UPS.</summary>
    UpsOnBattery,

    /// <summary>A UPS's battery charge, percent; evaluated per UPS.</summary>
    UpsBatteryCharge,

    /// <summary>How long a UPS's battery would last at the present load, seconds; evaluated per UPS.</summary>
    UpsBatteryRuntime,

    /// <summary>Share of a UPS's capacity in use, percent; evaluated per UPS.</summary>
    UpsLoad,

    /// <summary>
    /// A UPS's battery charge while it runs on battery, percent; evaluated per UPS. A UPS on line power has no
    /// reading, so a battery still charging after an outage never counts: this is the one to shut machines down on.
    /// </summary>
    UpsChargeOnBattery,

    /// <summary>A UPS's battery runtime while it runs on battery, seconds; evaluated per UPS.</summary>
    UpsRuntimeOnBattery,
}

/// <summary>How a rule decides that a reading is a problem.</summary>
public enum AlertCondition
{
    /// <summary>The reading passes a fixed threshold.</summary>
    Threshold,

    /// <summary>The reading strays from the host's usual level; the threshold counts standard deviations.</summary>
    Anomaly,
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

    public static bool IsContainer(this AlertMetric metric) => metric is AlertMetric.ContainerDown or AlertMetric.ContainerRestarts;

    /// <summary>Metrics read from a host's UPSes, evaluated once per UPS.</summary>
    public static bool IsUps(this AlertMetric metric) =>
        metric is AlertMetric.UpsOnBattery or AlertMetric.UpsBatteryCharge or AlertMetric.UpsBatteryRuntime or AlertMetric.UpsLoad
            || metric.IsOnBatteryOnly();

    /// <summary>UPS readings that only count while the UPS runs on battery, and clear once it is back on line power.</summary>
    public static bool IsOnBatteryOnly(this AlertMetric metric) =>
        metric is AlertMetric.UpsChargeOnBattery or AlertMetric.UpsRuntimeOnBattery;

    /// <summary>
    /// Metrics evaluated per resource (a mount point, a service, a container or a UPS), which a rule may
    /// narrow to one.
    /// </summary>
    public static bool IsPerResource(this AlertMetric metric) =>
        metric.IsPerFilesystem() || metric == AlertMetric.ServiceFailed || metric.IsContainer() || metric.IsUps();

    /// <summary>States rather than measurements: no threshold or direction, only how long they last.</summary>
    public static bool IsState(this AlertMetric metric) =>
        metric is AlertMetric.HostOffline or AlertMetric.ServiceFailed or AlertMetric.ContainerDown or AlertMetric.UpsOnBattery;

    /// <summary>
    /// Alerts that end when their resource stops being observed, such as a service no longer failing or a
    /// container back up, rather than when a measurement returns to normal.
    /// </summary>
    public static bool ResolvesWhenUnobserved(this AlertMetric metric) => metric is AlertMetric.ServiceFailed || metric.IsContainer();

    /// <summary>Host-wide measurements, which have the 5-minute rollup anomaly rules learn from.</summary>
    public static bool SupportsAnomaly(this AlertMetric metric) => !metric.IsState() && !metric.IsPerResource();

    public static bool IsPercentage(this AlertMetric metric) =>
        metric is AlertMetric.CpuUsage or AlertMetric.MemoryUsage or AlertMetric.SwapUsage
            or AlertMetric.DiskIoUtilization or AlertMetric.DiskUsage or AlertMetric.InodeUsage
            or AlertMetric.UpsBatteryCharge or AlertMetric.UpsLoad or AlertMetric.UpsChargeOnBattery;

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
        AlertMetric.HostOffline => "Host offline",
        AlertMetric.ServiceFailed => "Service failed",
        AlertMetric.ContainerDown => "Container down",
        AlertMetric.ContainerRestarts => "Container restarts",
        AlertMetric.UpsOnBattery => "UPS on battery",
        AlertMetric.UpsBatteryCharge => "UPS battery charge",
        AlertMetric.UpsBatteryRuntime => "UPS battery runtime",
        AlertMetric.UpsLoad => "UPS load",
        AlertMetric.UpsChargeOnBattery => "UPS charge on battery",
        AlertMetric.UpsRuntimeOnBattery => "UPS runtime on battery",
        _ => metric.ToString(),
    };
}
