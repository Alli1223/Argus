using System.Threading.Channels;
using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Power;

/// <summary>A host for an alert rule to shut down or wake, owned by the rule's owner.</summary>
public sealed record RulePowerRequest(Guid HostId, Guid OwnerId, PowerActionKind Kind, Guid RuleId, string Reason);

/// <summary>Power requests from alert rules, waiting for <see cref="RulePowerActionService"/>.</summary>
public sealed class RulePowerQueue
{
    private readonly Channel<RulePowerRequest> _requests = Channel.CreateUnbounded<RulePowerRequest>();

    public void Enqueue(RulePowerRequest request) => _requests.Writer.TryWrite(request);

    public IAsyncEnumerable<RulePowerRequest> ReadAllAsync(CancellationToken cancellationToken) => _requests.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// When an alert fires whose rule names hosts to shut down, shuts them down; when it resolves and the rule
/// says so, wakes them again. The requests are queued, so evaluating alerts never waits on agents.
/// </summary>
internal sealed class PowerAlertSink(ArgusDbContext db, RulePowerQueue queue) : IAlertEventSink
{
    public async Task PublishAsync(IReadOnlyList<AlertEvent> events, CancellationToken cancellationToken)
    {
        var alertIds = events.Select(alertEvent => alertEvent.AlertId).Distinct().ToList();
        var rules = await db.Alerts.AsNoTracking()
            .Where(alert => alertIds.Contains(alert.Id) && alert.Rule != null && alert.Rule.ShutdownHostIds.Count > 0)
            .Select(alert => new { alert.Id, alert.Rule!.Enabled, RuleId = alert.Rule.Id, alert.Rule.ShutdownHostIds, alert.Rule.WakeOnResolve })
            .ToDictionaryAsync(row => row.Id, cancellationToken);

        foreach (var alertEvent in events)
        {
            if (!rules.TryGetValue(alertEvent.AlertId, out var rule))
            {
                continue;
            }

            // A rule switched off resolves its alerts, which is no reason to wake anything.
            PowerActionKind? kind = alertEvent.Kind switch
            {
                AlertEventKind.Fired => PowerActionKind.Shutdown,
                AlertEventKind.Resolved when rule.WakeOnResolve && rule.Enabled => PowerActionKind.Wake,
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            var reason = kind == PowerActionKind.Shutdown ? alertEvent.Title : $"Resolved: {alertEvent.Title}";
            foreach (var hostId in rule.ShutdownHostIds.Distinct())
            {
                queue.Enqueue(new RulePowerRequest(hostId, alertEvent.OwnerId, kind.Value, rule.RuleId, reason));
            }
        }
    }
}

/// <summary>Carries out alert rules' power requests, each on its own, as they arrive.</summary>
internal sealed class RulePowerActionService(RulePowerQueue queue, IServiceScopeFactory scopes, ILogger<RulePowerActionService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken))
            {
                _ = CarryOutAsync(request, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CarryOutAsync(RulePowerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ArgusDbContext>();
            var power = scope.ServiceProvider.GetRequiredService<PowerService>();

            // Rules act on their owner's hosts only, whatever their list says.
            var host = await db.Hosts.AsNoTracking()
                .SingleOrDefaultAsync(h => h.Id == request.HostId && h.OwnerId == request.OwnerId, cancellationToken);
            if (host is null)
            {
                return;
            }

            if (request.Kind == PowerActionKind.Wake)
            {
                await power.WakeAsync(host, request.Reason, null, request.RuleId, cancellationToken);
            }
            else
            {
                await power.ShutdownAsync(host, request.Kind, request.Reason, null, request.RuleId, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not {Kind} host {HostId} for alert rule {RuleId}", request.Kind, request.HostId, request.RuleId);
        }
    }
}
