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
    ];

    private static AlertRule Rule(
        Guid ownerId, DateTimeOffset now, string name, AlertMetric metric, double threshold, AlertSeverity severity) => new()
    {
        OwnerId = ownerId,
        Name = name,
        Metric = metric,
        Operator = AlertOperator.Above,
        Threshold = threshold,
        DurationSeconds = (int)Sustained.TotalSeconds,
        Severity = severity,
        Enabled = true,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
