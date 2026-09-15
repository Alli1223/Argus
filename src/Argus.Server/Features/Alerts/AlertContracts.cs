using System.ComponentModel.DataAnnotations;
using Argus.Server.Features.Hosts;

namespace Argus.Server.Features.Alerts;

public sealed record AlertRuleResponse(
    Guid Id,
    string Name,
    AlertMetric Metric,
    AlertOperator Operator,
    double Threshold,
    int DurationSeconds,
    AlertSeverity Severity,
    Guid? HostId,
    string? HostName,
    string? Tag,
    string? ResourceFilter,
    bool Enabled,
    int FiringAlerts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Creates or replaces a rule.</summary>
public sealed record AlertRuleRequest
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = "";

    [Required]
    public AlertMetric? Metric { get; init; }

    public AlertOperator Operator { get; init; } = AlertOperator.Above;

    public double Threshold { get; init; }

    [Range(0, 86_400)]
    public int DurationSeconds { get; init; } = 300;

    public AlertSeverity Severity { get; init; } = AlertSeverity.Warning;

    /// <summary>Limit the rule to one of your hosts.</summary>
    public Guid? HostId { get; init; }

    /// <summary>Limit the rule to hosts with this tag.</summary>
    [MaxLength(HostTags.MaxLength)]
    public string? Tag { get; init; }

    /// <summary>For disk and inode rules: a single mount point.</summary>
    [MaxLength(256)]
    public string? ResourceFilter { get; init; }

    public bool Enabled { get; init; } = true;
}

public sealed record AlertResponse(
    Guid Id,
    Guid? RuleId,
    string? RuleName,
    Guid HostId,
    string HostName,
    string ResourceKey,
    string Title,
    AlertMetric Metric,
    AlertOperator Operator,
    double Threshold,
    AlertSeverity Severity,
    AlertStatus Status,
    double? Value,
    DateTimeOffset FiredAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedBy);

public sealed record AlertPage(IReadOnlyList<AlertResponse> Items, int Total, int Page, int PageSize);

/// <summary>Firing alerts by severity.</summary>
public sealed record AlertCounts(int Critical, int Warning, int Info)
{
    public int Total => Critical + Warning + Info;
}
