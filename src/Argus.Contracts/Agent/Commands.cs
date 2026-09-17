namespace Argus.Contracts.Agent;

/// <summary>What the server can ask an agent to do. There is deliberately nothing beyond these.</summary>
public enum AgentCommandKind
{
    ContainerStart,
    ContainerStop,
    ContainerRestart,
    ContainerLogs,
}

/// <summary>
/// A command for an agent to carry out and answer with an <see cref="AgentCommandResult"/>. Only agents
/// whose machine allows container actions ask for commands, and they check that setting again themselves.
/// </summary>
public sealed record AgentCommand
{
    public required string Id { get; init; }

    public required AgentCommandKind Kind { get; init; }

    /// <summary>The container's name.</summary>
    public required string Container { get; init; }

    /// <summary>For logs: how many of the newest lines to send.</summary>
    public int? Tail { get; init; }
}

public sealed record AgentCommandBatch
{
    public IReadOnlyList<AgentCommand> Commands { get; init; } = [];
}

public sealed record AgentCommandResult
{
    public required string Id { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>Why the command failed, in Docker's words where it said.</summary>
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
