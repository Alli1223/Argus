namespace Argus.Contracts.Agent;

/// <summary>Sent once by a new agent to exchange an enrollment token for credentials.</summary>
public sealed record RegisterAgentRequest
{
    public required string EnrollmentToken { get; init; }

    /// <summary>Stable, hashed identifier of the machine, used to re-link a reinstalled agent.</summary>
    public required string MachineId { get; init; }

    public required string AgentVersion { get; init; }

    public required SystemInfo SystemInfo { get; init; }
}

public sealed record RegisterAgentResponse
{
    public required Guid HostId { get; init; }

    /// <summary>Secret the agent must present as a bearer token on every later request.</summary>
    public required string AgentKey { get; init; }

    public required AgentSettings Settings { get; init; }
}

/// <summary>Periodic inventory update (system info may change, e.g. after upgrades).</summary>
public sealed record InventoryReport
{
    public required string AgentVersion { get; init; }

    public required SystemInfo SystemInfo { get; init; }
}

/// <summary>Settings the server pushes to agents in registration, inventory and metrics responses.</summary>
public sealed record AgentSettings
{
    public int CollectionIntervalSeconds { get; init; } = 15;

    public int InventoryIntervalMinutes { get; init; } = 60;

    public int TopProcessCount { get; init; } = 10;
}

/// <summary>A newer agent build for this machine, to fetch from <see cref="AgentApi.UpdateDownload"/>.</summary>
public sealed record AgentUpdateOffer
{
    public required string Version { get; init; }

    /// <summary>SHA-256 of the build, in lowercase hex. The agent must not run a download that does not match.</summary>
    public required string Sha256 { get; init; }

    public required long Size { get; init; }
}

public sealed record AgentUpdateResult
{
    /// <summary>The version the agent tried to install.</summary>
    public required string Version { get; init; }

    public required bool Succeeded { get; init; }

    public string? Error { get; init; }
}
