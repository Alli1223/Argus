using Argus.Server.Data;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Argus.Server.Features.Notifications;

/// <summary>Queues the daily and weekly reports that are due, once per period for each channel that wants them.</summary>
public sealed class ReportScheduler(
    ArgusDbContext db,
    NpgsqlDataSource dataSource,
    ReportBuilder builder,
    NotificationSignal signal,
    IOptions<ReportOptions> options,
    TimeProvider time)
{
    public async Task<int> QueueDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var queued = 0;
        foreach (var kind in Enum.GetValues<ReportKind>())
        {
            var due = ReportSchedule.LatestDue(kind, now, options.Value);
            var column = kind == ReportKind.Daily ? "daily" : "weekly";

            // Marking a report sent is also how it is claimed, so two servers cannot both queue it. After
            // downtime only the latest period is reported.
            List<ClaimedChannel> claimed;
            await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
            {
                claimed = [.. await connection.QueryAsync<ClaimedChannel>(new CommandDefinition($"""
                    UPDATE notification_channels
                    SET {column}_report_sent_for = @due
                    WHERE enabled AND {column}_report AND ({column}_report_sent_for IS NULL OR {column}_report_sent_for < @due)
                    RETURNING id, owner_id
                    """, new { due }, cancellationToken: cancellationToken))];
            }

            foreach (var owner in claimed.GroupBy(channel => channel.OwnerId))
            {
                var payload = (await builder.BuildAsync(owner.Key, kind, due, now, cancellationToken)).ToJson();
                foreach (var channel in owner)
                {
                    db.NotificationDeliveries.Add(new NotificationDelivery
                    {
                        ChannelId = channel.Id,
                        Kind = kind == ReportKind.Daily ? NotificationKind.DailyReport : NotificationKind.WeeklyReport,
                        Payload = payload,
                        Status = DeliveryStatus.Pending,
                        NextAttemptAt = now,
                        CreatedAt = now,
                    });
                    queued++;
                }
            }
        }

        if (queued > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            signal.Notify();
        }

        return queued;
    }

    private sealed class ClaimedChannel
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
    }
}

/// <summary>Checks for due reports every few minutes.</summary>
internal sealed class ReportSchedulingService(
    IServiceScopeFactory scopes,
    IOptions<ReportOptions> options,
    TimeProvider time,
    ILogger<ReportSchedulingService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundScheduling)
        {
            logger.LogInformation("Background report scheduling is switched off");
            return;
        }

        using var timer = new PeriodicTimer(CheckInterval, time);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ReportScheduler>().QueueDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Queuing reports failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
