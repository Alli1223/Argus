using Argus.Server.Features.Alerts;
using Argus.Server.Features.Hosts;

namespace Argus.Server.Features.Live;

/// <summary>A host reported new metrics.</summary>
public sealed record LiveHostMetrics(Guid HostId, LatestMetrics Latest);

/// <summary>A host went offline or came back.</summary>
public sealed record LiveHostStatus(Guid HostId, HostStatus Status, DateTimeOffset? LastSeenAt);

/// <summary>An alert fired or resolved.</summary>
public sealed record LiveAlert(
    AlertEventKind Kind,
    Guid AlertId,
    Guid HostId,
    string Title,
    AlertSeverity Severity,
    double? Value,
    DateTimeOffset At);
