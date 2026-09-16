using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Server.Features.Alerts;

namespace Argus.Server.Features.Notifications;

/// <summary>What a notification about an alert says, captured when the alert fired or resolved.</summary>
public sealed record AlertNotification(
    AlertEventKind Kind,
    Guid AlertId,
    Guid HostId,
    string HostName,
    string Title,
    AlertSeverity Severity,
    string Reading,
    string? RuleName,
    DateTimeOffset FiredAt,
    DateTimeOffset? ResolvedAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static AlertNotification From(AlertEventKind kind, Alert alert, string hostName, string? ruleName) => new(
        kind,
        alert.Id,
        alert.HostId,
        hostName,
        alert.Title,
        alert.Severity,
        DescribeReading(alert),
        ruleName,
        alert.FiredAt,
        alert.ResolvedAt);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AlertNotification FromJson(string json) =>
        JsonSerializer.Deserialize<AlertNotification>(json, Json) ?? throw new JsonException("The alert notification is empty.");

    /// <summary>The reading behind an alert in words: "93.2%, threshold 90%", "60%, usually 10.2%".</summary>
    public static string DescribeReading(Alert alert) => alert switch
    {
        { Metric: AlertMetric.HostOffline } => alert.Value is { } silence
            ? $"No report for {DescribeDuration(TimeSpan.FromSeconds(silence))}"
            : "Has never reported",
        { Metric: AlertMetric.ServiceFailed } => $"{alert.ResourceKey} has failed",
        { Condition: AlertCondition.Anomaly } => $"{Format(alert, alert.Value)}, usually {Format(alert, alert.Baseline)}",
        _ => $"{Format(alert, alert.Value)}, threshold {Format(alert, alert.Threshold)}",
    };

    /// <summary>A rough duration: "45 seconds", "1 minute", "3 hours", "2 days".</summary>
    public static string DescribeDuration(TimeSpan span)
    {
        var (amount, unit) = span.TotalSeconds switch
        {
            < 60 => ((int)span.TotalSeconds, "second"),
            < 3600 => ((int)span.TotalMinutes, "minute"),
            < 86400 => ((int)span.TotalHours, "hour"),
            _ => ((int)span.TotalDays, "day"),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{amount} {unit}{(amount == 1 ? "" : "s")}");
    }

    private static string Format(Alert alert, double? value) =>
        value is { } number ? AlertDecision.FormatThreshold(alert.Metric, number) : "no reading";
}
