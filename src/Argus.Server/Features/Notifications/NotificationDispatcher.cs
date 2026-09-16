using Argus.Server.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Argus.Server.Features.Notifications;

public sealed record DispatchResult(int Sent, int Retrying, int Failed)
{
    public int Total => Sent + Retrying + Failed;
}

/// <summary>
/// Sends the notifications that are due. Each batch is claimed with row locks and a lease, so several
/// servers can share the queue, and a server that dies mid-send only delays a delivery until the lease
/// runs out. Failed sends are tried again with growing gaps until <see cref="NotificationOptions.MaxAttempts"/>.
/// </summary>
public sealed class NotificationDispatcher(
    ArgusDbContext db,
    NpgsqlDataSource dataSource,
    IEnumerable<INotificationSender> senders,
    IOptions<NotificationOptions> options,
    TimeProvider time,
    ILogger<NotificationDispatcher> logger)
{
    public const int BatchSize = 20;

    /// <summary>How long a claimed delivery is left to this server before another may try it.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <summary>The wait before another attempt: 1 minute, 5 minutes, 15 minutes, 1 hour, then 4 hours.</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromHours(1),
        _ => TimeSpan.FromHours(4),
    };

    public async Task<DispatchResult> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        List<Guid> claimed;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            claimed = [.. await connection.QueryAsync<Guid>(new CommandDefinition("""
                UPDATE notification_deliveries d
                SET attempts = d.attempts + 1, next_attempt_at = @lease_until
                FROM (
                    SELECT id FROM notification_deliveries
                    WHERE status = 'Pending' AND next_attempt_at <= @now
                    ORDER BY next_attempt_at
                    LIMIT @batch
                    FOR UPDATE SKIP LOCKED
                ) due
                WHERE d.id = due.id
                RETURNING d.id
                """,
                new { now, lease_until = now + Lease, batch = BatchSize },
                cancellationToken: cancellationToken))];
        }

        if (claimed.Count == 0)
        {
            return new DispatchResult(0, 0, 0);
        }

        var deliveries = await db.NotificationDeliveries
            .Include(delivery => delivery.Channel)
            .Where(delivery => claimed.Contains(delivery.Id))
            .OrderBy(delivery => delivery.CreatedAt)
            .ToListAsync(cancellationToken);
        var byKind = senders.ToDictionary(sender => sender.Kind);

        int sent = 0, retrying = 0, failed = 0;
        foreach (var delivery in deliveries)
        {
            switch (await SendAsync(delivery, byKind, cancellationToken))
            {
                case DeliveryStatus.Sent:
                    sent++;
                    break;
                case DeliveryStatus.Pending:
                    retrying++;
                    break;
                default:
                    failed++;
                    break;
            }

            // Record each outcome as it happens, so a later failure cannot cause a repeat send.
            await db.SaveChangesAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        return new DispatchResult(sent, retrying, failed);
    }

    /// <summary>Forgets sent and failed notifications older than <see cref="NotificationOptions.KeepDays"/>.</summary>
    public Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().AddDays(-options.Value.KeepDays);
        return db.NotificationDeliveries
            .Where(delivery => delivery.Status != DeliveryStatus.Pending && delivery.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<DeliveryStatus> SendAsync(
        NotificationDelivery delivery, Dictionary<NotificationChannelKind, INotificationSender> byKind, CancellationToken cancellationToken)
    {
        var channel = delivery.Channel!;
        string? error = null;
        var final = false;

        if (!channel.Enabled)
        {
            (error, final) = ("The channel was switched off before this could be sent.", true);
        }
        else if (!byKind.TryGetValue(channel.Kind, out var sender))
        {
            (error, final) = ($"This server cannot send to {channel.Kind} channels.", true);
        }
        else
        {
            try
            {
                await sender.SendAsync(channel, delivery, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                error = ex.Message;
                logger.LogWarning(ex, "Sending notification {DeliveryId} to channel {ChannelId} ({ChannelName}) failed on attempt {Attempt}",
                    delivery.Id, channel.Id, channel.Name, delivery.Attempts);
            }
        }

        var now = time.GetUtcNow();
        if (error is null)
        {
            delivery.Status = DeliveryStatus.Sent;
            delivery.SentAt = now;
            delivery.LastError = null;
            return DeliveryStatus.Sent;
        }

        delivery.LastError = error.Length > NotificationDeliveryConfiguration.MaxErrorLength
            ? error[..NotificationDeliveryConfiguration.MaxErrorLength]
            : error;

        if (final || delivery.Attempts >= options.Value.MaxAttempts)
        {
            delivery.Status = DeliveryStatus.Failed;
            return DeliveryStatus.Failed;
        }

        delivery.NextAttemptAt = now + RetryDelay(delivery.Attempts);
        return DeliveryStatus.Pending;
    }
}
