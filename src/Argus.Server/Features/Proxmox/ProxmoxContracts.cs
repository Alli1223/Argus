namespace Argus.Server.Features.Proxmox;

/// <summary>A Proxmox VE guest as the web app lists it.</summary>
/// <param name="StatusSince">When its status last changed, as far as Argus saw.</param>
public sealed record ProxmoxGuestInfo(
    int Id,
    string Type,
    string Name,
    string Status,
    string? Lock,
    bool Template,
    long UptimeSeconds,
    double? CpuPercent,
    double? Cpus,
    long? MemoryBytes,
    long? MaxMemoryBytes,
    long? MaxDiskBytes,
    IReadOnlyList<string> Tags,
    DateTimeOffset StatusSince);

/// <summary>
/// A host's Proxmox VE guests, from its agent's newest report. <see cref="CheckedAt"/> is null for hosts
/// whose agent has never reported any; <see cref="Problem"/> says why the agent could not read Proxmox VE.
/// </summary>
public sealed record HostProxmox(
    DateTimeOffset? CheckedAt,
    string? Node,
    string? Version,
    string? Problem,
    bool ActionsEnabled,
    bool Listening,
    IReadOnlyList<ProxmoxGuestInfo> Guests);

public enum GuestAction
{
    Start,
    Shutdown,
    Reboot,
    Stop,
}
