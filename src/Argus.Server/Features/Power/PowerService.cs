using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Containers;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Power;

public enum PowerProblem
{
    /// <summary>The machine's agent does not allow it.</summary>
    NotAllowed,

    /// <summary>The machine's agent is not connected for commands.</summary>
    NotListening,

    /// <summary>Argus knows no hardware address to wake the machine by.</summary>
    NoAddress,

    /// <summary>No other agent is connected to send the Wake-on-LAN packet.</summary>
    NoRelay,

    /// <summary>The agent did not answer in time.</summary>
    NoAnswer,

    /// <summary>The agent tried and failed.</summary>
    Refused,
}

public sealed record PowerOutcome(PowerAction Action, PowerProblem? Problem);

/// <summary>
/// Shuts hosts down, restarts them and wakes them, for people in the web app and for alert rules, and keeps
/// a record of each time. Shutting down goes through the host's own agent; waking goes through the agents of
/// the owner's other machines, as only a machine on the same network can wake another.
/// </summary>
public sealed class PowerService(ArgusDbContext db, AgentCommandBroker broker, TimeProvider time, ILogger<PowerService> logger)
{
    /// <summary>How long to wait for an agent to say it has started shutting down.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan WakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>At most this many agents send each Wake-on-LAN packet.</summary>
    public const int MaxRelays = 3;

    public async Task<PowerOutcome> ShutdownAsync(
        MonitoredHost host, PowerActionKind kind, string reason, string? requestedBy, Guid? ruleId, CancellationToken cancellationToken)
    {
        var commandKind = kind == PowerActionKind.Restart ? AgentCommandKind.Restart : AgentCommandKind.Shutdown;
        (PowerProblem? Problem, string? Detail) outcome;
        if (!host.AgentCommands.Contains(commandKind.ToString()))
        {
            outcome = (PowerProblem.NotAllowed,
                "Shutting down and restarting are off on this machine. Run the agent's install command on it again with power actions allowed.");
        }
        else if (!broker.IsListening(host.Id))
        {
            outcome = (PowerProblem.NotListening, "The machine's agent is not connected. The machine may be off already.");
        }
        else
        {
            var command = new AgentCommand { Id = Guid.NewGuid().ToString(), Kind = commandKind, Reason = reason };
            outcome = await broker.SendAsync(host.Id, command, ShutdownTimeout, cancellationToken) switch
            {
                null => (PowerProblem.NoAnswer, "The agent did not answer in time. The machine may be shutting down anyway."),
                { Succeeded: false } failed => (PowerProblem.Refused, failed.Error ?? "The agent could not do it."),
                _ => (null, null),
            };
        }

        return await RecordAsync(host, kind, reason, requestedBy, ruleId, outcome, cancellationToken);
    }

    public async Task<PowerOutcome> WakeAsync(MonitoredHost host, string reason, string? requestedBy, Guid? ruleId, CancellationToken cancellationToken)
    {
        List<string> macs = host.WakeMacAddress is { } mac ? [mac] : host.MacAddresses;
        List<string> addresses = [.. host.WakeAddress is { } address ? [address] : Array.Empty<string>(), .. host.Networks];
        (PowerProblem? Problem, string? Detail) outcome;
        if (macs.Count == 0)
        {
            outcome = (PowerProblem.NoAddress,
                "Argus does not know this machine's hardware address. Update its agent, which reports it, or enter it under Wake-on-LAN.");
        }
        else if (await RelaysAsync(host, addresses, cancellationToken) is not { Count: > 0 } relays)
        {
            outcome = (PowerProblem.NoRelay,
                "None of your other machines' agents is connected to send the packet. Wake-on-LAN needs one on the same network.");
        }
        else
        {
            var command = new AgentCommand { Id = "", Kind = AgentCommandKind.WakeOnLan, MacAddresses = macs, Addresses = addresses };
            var answers = await Task.WhenAll(relays.Select(relay =>
                broker.SendAsync(relay.Id, command with { Id = Guid.NewGuid().ToString() }, WakeTimeout, cancellationToken)));
            var sentBy = relays.Where((_, i) => answers[i] is { Succeeded: true }).Select(relay => relay.DisplayName).ToList();
            outcome = sentBy.Count > 0
                ? (null, $"Sent by {string.Join(", ", sentBy)}")
                : answers.FirstOrDefault(answer => answer is { Succeeded: false }) is { } failed
                    ? (PowerProblem.Refused, failed.Error ?? "The agents could not send it.")
                    : (PowerProblem.NoAnswer, "No agent answered in time.");
        }

        return await RecordAsync(host, PowerActionKind.Wake, reason, requestedBy, ruleId, outcome, cancellationToken);
    }

    /// <summary>
    /// The owner's other machines whose agents are connected and send Wake-on-LAN packets, those on the same
    /// network as the machine first. Without any there, the others try: some routers pass broadcasts on.
    /// </summary>
    public async Task<List<(Guid Id, string DisplayName)>> RelaysAsync(MonitoredHost host, List<string> addresses, CancellationToken cancellationToken)
    {
        var candidates = (await db.Hosts.AsNoTracking()
                .Where(other => other.OwnerId == host.OwnerId && other.Id != host.Id)
                .Select(other => new { other.Id, other.DisplayName, other.AgentCommands, other.Networks })
                .ToListAsync(cancellationToken))
            .Where(other => other.AgentCommands.Contains(nameof(AgentCommandKind.WakeOnLan)) && broker.IsListening(other.Id))
            .ToList();
        var nearby = candidates.Where(other => PowerAddresses.ShareNetwork(other.Networks, addresses)).ToList();
        return (nearby.Count > 0 ? nearby : candidates)
            .Take(MaxRelays)
            .Select(other => (other.Id, other.DisplayName))
            .ToList();
    }

    public Task<List<PowerActionInfo>> HistoryAsync(Guid hostId, int count, CancellationToken cancellationToken) =>
        db.PowerActions.AsNoTracking()
            .Where(action => action.HostId == hostId)
            .OrderByDescending(action => action.At)
            .Take(count)
            .Select(action => new PowerActionInfo(
                action.Id, action.Kind, action.RequestedBy, action.RuleId, action.Rule != null ? action.Rule.Name : null,
                action.Reason, action.Succeeded, action.Detail, action.At))
            .ToListAsync(cancellationToken);

    private async Task<PowerOutcome> RecordAsync(
        MonitoredHost host,
        PowerActionKind kind,
        string reason,
        string? requestedBy,
        Guid? ruleId,
        (PowerProblem? Problem, string? Detail) outcome,
        CancellationToken cancellationToken)
    {
        var action = new PowerAction
        {
            HostId = host.Id,
            Kind = kind,
            RequestedBy = requestedBy,
            RuleId = ruleId,
            Reason = Clip(reason, PowerActionConfiguration.ReasonLength)!,
            Succeeded = outcome.Problem is null,
            Detail = Clip(outcome.Detail, PowerActionConfiguration.DetailLength),
            At = time.GetUtcNow(),
        };
        db.PowerActions.Add(action);
        await db.SaveChangesAsync(cancellationToken);

        if (outcome.Problem is null)
        {
            logger.LogWarning("{Kind} {Host} ({Reason}), asked by {Asker}", kind, host.DisplayName, reason, requestedBy ?? "an alert rule");
        }
        else
        {
            logger.LogWarning("Could not {Kind} {Host}: {Detail}", kind, host.DisplayName, outcome.Detail);
        }

        return new PowerOutcome(action, outcome.Problem);
    }

    private static string? Clip(string? value, int length) => value is null || value.Length <= length ? value : value[..length];
}
