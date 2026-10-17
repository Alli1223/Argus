using Argus.Server.Data;
using Argus.Server.Features.Live;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Alerts;

public enum AlertActionOutcome
{
    Done,

    /// <summary>The alert had already been acknowledged or ignored; nothing changed.</summary>
    AlreadyDone,

    /// <summary>Only firing alerts can be acknowledged.</summary>
    NotFiring,

    /// <summary>The alert's rule was deleted, so there is nothing to stop watching.</summary>
    NoRule,
}

/// <summary>What people do to an alert, from the web app or a chat: acknowledge it, or ignore it.</summary>
public sealed class AlertActions(ArgusDbContext db, LiveUpdates live, TimeProvider time)
{
    public async Task<AlertActionOutcome> AcknowledgeAsync(Alert alert, Guid userId, CancellationToken cancellationToken)
    {
        if (alert.Status != AlertStatus.Firing)
        {
            return AlertActionOutcome.NotFiring;
        }

        if (alert.AcknowledgedAt is not null)
        {
            return AlertActionOutcome.AlreadyDone;
        }

        alert.AcknowledgedAt = time.GetUtcNow();
        alert.AcknowledgedById = userId;
        await db.SaveChangesAsync(cancellationToken);
        return AlertActionOutcome.Done;
    }

    /// <summary>
    /// Stops the alert's rule watching its host and resource, and closes the alert if it is firing. Closing it
    /// this way sends no "resolved" notification, since nothing recovered; browsers are still told.
    /// </summary>
    public async Task<(AlertActionOutcome Outcome, AlertIgnore? Ignore)> IgnoreAsync(
        Alert alert, Guid userId, CancellationToken cancellationToken)
    {
        if (alert.RuleId is not { } ruleId)
        {
            return (AlertActionOutcome.NoRule, null);
        }

        var now = time.GetUtcNow();
        var ignore = await db.AlertIgnores.SingleOrDefaultAsync(
            i => i.RuleId == ruleId && i.HostId == alert.HostId && i.ResourceKey == alert.ResourceKey, cancellationToken);
        var outcome = AlertActionOutcome.AlreadyDone;
        if (ignore is null)
        {
            ignore = new AlertIgnore
            {
                RuleId = ruleId,
                HostId = alert.HostId,
                OwnerId = alert.OwnerId,
                ResourceKey = alert.ResourceKey,
                Title = alert.Title,
                CreatedAt = now,
                CreatedById = userId,
            };
            db.AlertIgnores.Add(ignore);
            outcome = AlertActionOutcome.Done;
        }

        var closed = alert.Status == AlertStatus.Firing;
        if (closed)
        {
            alert.Status = AlertStatus.Resolved;
            alert.ResolvedAt = now;
            alert.IgnoredAt = now;
            outcome = AlertActionOutcome.Done;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (outcome == AlertActionOutcome.Done)
        {
            // Someone ignored the same thing at the same moment: theirs stands.
            db.ChangeTracker.Clear();
            var existing = await db.AlertIgnores.AsNoTracking().SingleAsync(
                i => i.RuleId == ruleId && i.HostId == alert.HostId && i.ResourceKey == alert.ResourceKey, cancellationToken);
            return (AlertActionOutcome.AlreadyDone, existing);
        }

        if (closed)
        {
            await live.AlertChangedAsync(alert.OwnerId, new LiveAlert(
                AlertEventKind.Resolved, alert.Id, alert.HostId, alert.Title, alert.Severity, alert.Value, now));
        }

        return (outcome, ignore);
    }
}
