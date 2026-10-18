namespace Argus.Contracts.Agent;

/// <summary>What the server can ask an agent to do. There is deliberately nothing beyond these.</summary>
public enum AgentCommandKind
{
    ContainerStart,
    ContainerStop,
    ContainerRestart,
    ContainerLogs,

    /// <summary>Power the machine off. Only on machines whose agent configuration sets PowerActions.</summary>
    Shutdown,

    /// <summary>Restart the machine. Only on machines whose agent configuration sets PowerActions.</summary>
    Restart,

    /// <summary>Send a Wake-on-LAN packet onto the agent's own network, to wake another machine.</summary>
    WakeOnLan,

    /// <summary>Start a Proxmox VE virtual machine or container. Only where the agent configuration sets ProxmoxActions.</summary>
    GuestStart,

    /// <summary>Ask a Proxmox VE guest to shut down cleanly, as its power button would.</summary>
    GuestShutdown,

    /// <summary>Ask a Proxmox VE guest to restart cleanly.</summary>
    GuestReboot,

    /// <summary>Stop a Proxmox VE guest at once, as pulling its plug would.</summary>
    GuestStop,
}

/// <summary>
/// A command for an agent to carry out and answer with an <see cref="AgentCommandResult"/>. Agents say which
/// kinds they carry out in their inventory (<see cref="InventoryReport.Commands"/>) and check their own
/// settings again before acting.
/// </summary>
public sealed record AgentCommand
{
    public required string Id { get; init; }

    public required AgentCommandKind Kind { get; init; }

    /// <summary>For container commands: the container's name.</summary>
    public string Container { get; init; } = "";

    /// <summary>For logs: how many of the newest lines to send.</summary>
    public int? Tail { get; init; }

    /// <summary>For Wake-on-LAN: the hardware addresses of the machine to wake, as <c>aa:bb:cc:dd:ee:ff</c>.</summary>
    public IReadOnlyList<string>? MacAddresses { get; init; }

    /// <summary>
    /// For Wake-on-LAN: the machine's last known addresses with their prefix length (<c>192.168.1.20/24</c>),
    /// or broadcast addresses to send to, which tell the agent which of its networks the machine is on.
    /// </summary>
    public IReadOnlyList<string>? Addresses { get; init; }

    /// <summary>For shutdown and restart: why, for the machine's own logs.</summary>
    public string? Reason { get; init; }

    /// <summary>For Proxmox VE guests: the guest's id (VMID), such as 100.</summary>
    public int? GuestId { get; init; }

    /// <summary>For Proxmox VE guests: <c>qemu</c> for a virtual machine, <c>lxc</c> for a container.</summary>
    public string? GuestType { get; init; }
}

public sealed record AgentCommandBatch
{
    public IReadOnlyList<AgentCommand> Commands { get; init; } = [];
}

public sealed record AgentCommandResult
{
    public required string Id { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>Why the command failed, in Docker's or Proxmox VE's words where they said.</summary>
    public string? Error { get; init; }

    /// <summary>For logs: the lines, oldest first.</summary>
    public IReadOnlyList<ContainerLogLine>? Logs { get; init; }

    /// <summary>For logs: whether lines were left out to keep the answer small.</summary>
    public bool Truncated { get; init; }
}

public sealed record ContainerLogLine
{
    public DateTimeOffset? Time { get; init; }

    /// <summary>stdout or stderr.</summary>
    public required string Stream { get; init; }

    public required string Text { get; init; }
}
