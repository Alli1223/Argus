using System.Text.Json;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Containers;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Proxmox;

public enum GuestProblem
{
    /// <summary>The host reports no guest with that id.</summary>
    NotFound,

    /// <summary>The machine's agent does not allow it.</summary>
    NotAllowed,

    /// <summary>The machine's agent is not connected for commands.</summary>
    NotListening,

    /// <summary>The agent did not answer in time.</summary>
    NoAnswer,

    /// <summary>Proxmox VE refused.</summary>
    Refused,
}

/// <summary>Hosts' Proxmox VE guests: storing what agents report, and starting and stopping guests through them.</summary>
public sealed class ProxmoxService(ArgusDbContext db, AgentCommandBroker broker, ILogger<ProxmoxService> logger)
{
    /// <summary>The agent watches Proxmox VE's task for 10 seconds; this leaves room for the round trips.</summary>
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Applies a host's report of its guests, unless a newer one is already stored. When the agent could not
    /// read Proxmox VE, the guests last seen are kept as they were.
    /// </summary>
    public async Task ApplyReportAsync(Guid hostId, DateTimeOffset checkedAt, ProxmoxReport report, CancellationToken cancellationToken)
    {
        var check = await db.ProxmoxChecks.SingleOrDefaultAsync(c => c.HostId == hostId, cancellationToken);
        if (check is not null && check.CheckedAt >= checkedAt)
        {
            return;
        }

        if (check is null)
        {
            check = new ProxmoxCheck { HostId = hostId };
            db.Add(check);
        }

        check.CheckedAt = checkedAt;
        check.Problem = report.Problem;
        check.ActionsEnabled = report.ActionsEnabled;
        if (report.Problem is null)
        {
            var previous = Read(check.Guests).ToDictionary(guest => guest.Id);
            check.Node = report.Node;
            check.Version = report.Version;
            check.Guests = JsonSerializer.Serialize(
                report.Guests.Select(guest => ToInfo(guest, previous.GetValueOrDefault(guest.Id) is { } before && before.Status == guest.Status
                    ? before.StatusSince
                    : checkedAt)).ToList(),
                JsonSerializerOptions.Web);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<HostProxmox> GetAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var check = await db.ProxmoxChecks.AsNoTracking().SingleOrDefaultAsync(c => c.HostId == hostId, cancellationToken);
        return check is null
            ? new HostProxmox(null, null, null, null, false, broker.IsListening(hostId), [])
            : new HostProxmox(check.CheckedAt, check.Node, check.Version, check.Problem, check.ActionsEnabled, broker.IsListening(hostId), Read(check.Guests));
    }

    /// <summary>Asks the host's agent to start, shut down, restart or stop a guest; null when it did.</summary>
    public async Task<(GuestProblem Problem, string Detail)?> ActAsync(
        Guid hostId, int guestId, GuestAction action, string? requestedBy, CancellationToken cancellationToken)
    {
        var check = await db.ProxmoxChecks.AsNoTracking().SingleOrDefaultAsync(c => c.HostId == hostId, cancellationToken);
        if (check is null || Read(check.Guests).SingleOrDefault(guest => guest.Id == guestId) is not { } guest)
        {
            return (GuestProblem.NotFound, "This machine reports no guest with that id.");
        }

        if (!check.ActionsEnabled)
        {
            return (GuestProblem.NotAllowed,
                "Starting and stopping guests is off on this machine. Run the agent's install command on it again with --proxmox-actions.");
        }

        if (!broker.IsListening(hostId))
        {
            return (GuestProblem.NotListening, "The machine's agent is not connected right now. It may be offline or restarting.");
        }

        var command = new AgentCommand
        {
            Id = Guid.NewGuid().ToString(),
            Kind = action switch
            {
                GuestAction.Start => AgentCommandKind.GuestStart,
                GuestAction.Shutdown => AgentCommandKind.GuestShutdown,
                GuestAction.Reboot => AgentCommandKind.GuestReboot,
                _ => AgentCommandKind.GuestStop,
            },
            GuestId = guest.Id,
            GuestType = guest.Type,
        };
        logger.LogInformation("{Action} guest {GuestId} ({Name}) on host {HostId}, asked by {User}", action, guest.Id, guest.Name, hostId, requestedBy);
        return await broker.SendAsync(hostId, command, ActionTimeout, cancellationToken) switch
        {
            null => (GuestProblem.NoAnswer, "It may still happen. Check the guest again in a moment."),
            { Succeeded: false } failed => (GuestProblem.Refused, failed.Error ?? "The agent could not do it."),
            _ => null,
        };
    }

    private static ProxmoxGuestInfo ToInfo(ProxmoxGuest guest, DateTimeOffset statusSince) => new(
        guest.Id, guest.Type, guest.Name, guest.Status, guest.Lock, guest.Template, guest.UptimeSeconds, guest.CpuPercent, guest.Cpus,
        guest.MemoryBytes, guest.MaxMemoryBytes, guest.MaxDiskBytes, guest.Tags, statusSince);

    private static List<ProxmoxGuestInfo> Read(string json) =>
        JsonSerializer.Deserialize<List<ProxmoxGuestInfo>>(json, JsonSerializerOptions.Web) ?? [];
}
