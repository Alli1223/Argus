using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Notifications;

/// <summary>Queues a notification for each of the owner's channels that wants an alert that fired or resolved.</summary>
internal sealed class NotificationAlertSink(ArgusDbContext db, NotificationSignal signal, TimeProvider time) : IAlertEventSink
{
    public async Task PublishAsync(IReadOnlyList<AlertEvent> events, CancellationToken cancellationToken)
    {
        var ownerIds = events.Select(alertEvent => alertEvent.OwnerId).Distinct().ToList();
        var channels = await db.NotificationChannels.AsNoTracking()
            .Where(channel => channel.Enabled && ownerIds.Contains(channel.OwnerId))
            .ToListAsync(cancellationToken);
        if (channels.Count == 0)
        {
            return;
        }

        var alertIds = events.Select(alertEvent => alertEvent.AlertId).Distinct().ToList();
        var alerts = await db.Alerts.AsNoTracking()
            .Where(alert => alertIds.Contains(alert.Id))
            .Select(alert => new { Alert = alert, HostName = alert.Host!.DisplayName, RuleName = alert.Rule != null ? alert.Rule.Name : null })
            .ToDictionaryAsync(row => row.Alert.Id, cancellationToken);

        var now = time.GetUtcNow();
        var queued = 0;
        foreach (var alertEvent in events)
        {
            if (!alerts.TryGetValue(alertEvent.AlertId, out var row))
            {
                continue;
            }

            var payload = AlertNotification.From(alertEvent.Kind, row.Alert, row.HostName, row.RuleName).ToJson();
            foreach (var channel in channels.Where(c => c.OwnerId == alertEvent.OwnerId && c.Wants(alertEvent.Kind, alertEvent.Severity)))
            {
                db.NotificationDeliveries.Add(new NotificationDelivery
                {
                    ChannelId = channel.Id,
                    Kind = alertEvent.Kind == AlertEventKind.Fired ? NotificationKind.AlertFired : NotificationKind.AlertResolved,
                    Payload = payload,
                    Status = DeliveryStatus.Pending,
                    NextAttemptAt = now,
                    CreatedAt = now,
                });
                queued++;
            }
        }

        if (queued > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            signal.Notify();
        }
    }
}
