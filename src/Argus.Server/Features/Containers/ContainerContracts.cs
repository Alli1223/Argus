namespace Argus.Server.Features.Containers;

/// <summary>What a running container used in its newest sample.</summary>
public sealed record ContainerUsageSnapshot(
    DateTimeOffset Time, double CpuPercent, long MemoryBytes, long? MemoryLimitBytes, double? NetRxBytesPerSec, double? NetTxBytesPerSec);

/// <summary>A container as the web app lists it.</summary>
/// <param name="StateSince">When its state or health last changed, as far as Argus saw.</param>
/// <param name="RestartsLastHour">How often Docker restarted it in the past hour.</param>
public sealed record ContainerSummary(
    string Name,
    string Id,
    string Image,
    string State,
    string? Health,
    int RestartCount,
    int RestartsLastHour,
    int? ExitCode,
    bool OomKilled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset StateSince,
    string? RestartPolicy,
    string? ComposeProject,
    string? ComposeService,
    IReadOnlyList<string> Ports,
    ContainerUsageSnapshot? Usage);

/// <summary>
/// A host's containers, from its agent's newest report. <see cref="CheckedAt"/> is null for hosts whose
/// agent has never reported any; <see cref="Problem"/> says why the agent could not read Docker.
/// </summary>
public sealed record HostContainers(
    DateTimeOffset? CheckedAt,
    string? EngineVersion,
    string? Problem,
    bool ActionsEnabled,
    IReadOnlyList<ContainerSummary> Containers);

public sealed record ContainerEventInfo(DateTimeOffset Time, string Kind, string? Detail, int Count);

public sealed record ContainerDetail(
    Guid HostId,
    string HostName,
    bool ActionsEnabled,
    ContainerSummary Container,
    IReadOnlyList<ContainerEventInfo> Events);

/// <summary>A host that reports containers, for the page that lists every host's.</summary>
public sealed record ContainerHost(
    Guid HostId, string HostName, DateTimeOffset CheckedAt, string? Problem, bool ActionsEnabled, IReadOnlyList<ContainerSummary> Containers);
