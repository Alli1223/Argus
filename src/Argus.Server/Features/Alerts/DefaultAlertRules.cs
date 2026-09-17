namespace Argus.Server.Features.Alerts;

/// <summary>The rules every new account starts with, so new hosts are watched straight away.</summary>
public static class DefaultAlertRules
{
    private static readonly TimeSpan Sustained = TimeSpan.FromMinutes(5);

    public static List<AlertRule> For(Guid ownerId, DateTimeOffset now) =>
    [
        Rule(ownerId, now, "High CPU usage", AlertMetric.CpuUsage, 90, AlertSeverity.Warning),
        Rule(ownerId, now, "High memory usage", AlertMetric.MemoryUsage, 90, AlertSeverity.Warning),
        Rule(ownerId, now, "Disk almost full", AlertMetric.DiskUsage, 90, AlertSeverity.Critical),
        Rule(ownerId, now, "Host offline", AlertMetric.HostOffline, 0, AlertSeverity.Critical),

        // A failed service is already a settled state rather than a noisy reading, so it alerts at once.
        Rule(ownerId, now, "Service failed", AlertMetric.ServiceFailed, 0, AlertSeverity.Warning, TimeSpan.Zero),

        // Over every container these only fire for crashes, failing health checks and restart loops, not for
        // containers people stopped, so they are quiet on hosts without Docker and on healthy ones.
        Rule(ownerId, now, "Container down", AlertMetric.ContainerDown, 0, AlertSeverity.Warning, TimeSpan.FromMinutes(2)),
        Rule(ownerId, now, "Container restart loop", AlertMetric.ContainerRestarts, 3, AlertSeverity.Warning, TimeSpan.FromMinutes(10)),
    ];

    private static AlertRule Rule(
        Guid ownerId,
        DateTimeOffset now,
        string name,
        AlertMetric metric,
        double threshold,
        AlertSeverity severity,
        TimeSpan? duration = null) => new()
    {
        OwnerId = ownerId,
        Name = name,
        Metric = metric,
        Operator = AlertOperator.Above,
        Threshold = threshold,
        DurationSeconds = (int)(duration ?? Sustained).TotalSeconds,
        Severity = severity,
        Enabled = true,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
