using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Power;

/// <summary>What Argus can do about a host's power, and what it has done.</summary>
public sealed record HostPower(
    bool PowerActionsEnabled,
    bool Listening,
    IReadOnlyList<string> MacAddresses,
    IReadOnlyList<string> Networks,
    string? WakeMacAddress,
    string? WakeAddress,
    int WakeRelays,
    IReadOnlyList<PowerActionInfo> History);

public sealed record PowerActionInfo(
    Guid Id,
    PowerActionKind Kind,
    string? RequestedBy,
    Guid? RuleId,
    string? RuleName,
    string Reason,
    bool Succeeded,
    string? Detail,
    DateTimeOffset At);

/// <summary>How to wake a host, where its agent's report is not enough. Empty values go back to the agent's.</summary>
public sealed record PowerSettingsRequest
{
    /// <summary>The hardware address to wake the machine by, such as aa:bb:cc:dd:ee:ff.</summary>
    [MaxLength(64)]
    public string? WakeMacAddress { get; init; }

    /// <summary>Where to send the packet: the network's broadcast address, or the machine's address with its prefix.</summary>
    [MaxLength(64)]
    public string? WakeAddress { get; init; }
}
