namespace Argus.Server.Features.Alerts;

public enum AlertEventKind
{
    Fired,
    Resolved,
}

public sealed record AlertEvent(
    AlertEventKind Kind,
    Guid AlertId,
    Guid OwnerId,
    Guid HostId,
    string Title,
    AlertSeverity Severity,
    double? Value,
    DateTimeOffset At)
{
    public static AlertEvent From(AlertEventKind kind, Alert alert, DateTimeOffset at) =>
        new(kind, alert.Id, alert.OwnerId, alert.HostId, alert.Title, alert.Severity, alert.Value, at);
}

/// <summary>Receives alert state changes after they are saved (live updates, notifications, …).</summary>
public interface IAlertEventSink
{
    Task PublishAsync(IReadOnlyList<AlertEvent> events, CancellationToken cancellationToken);
}

internal sealed class LoggingAlertEventSink(ILogger<LoggingAlertEventSink> logger) : IAlertEventSink
{
    public Task PublishAsync(IReadOnlyList<AlertEvent> events, CancellationToken cancellationToken)
    {
        foreach (var alertEvent in events)
        {
            if (alertEvent.Kind == AlertEventKind.Fired)
            {
                logger.LogWarning("Alert fired ({Severity}): {Title}", alertEvent.Severity, alertEvent.Title);
            }
            else
            {
                logger.LogInformation("Alert resolved: {Title}", alertEvent.Title);
            }
        }

        return Task.CompletedTask;
    }
}
