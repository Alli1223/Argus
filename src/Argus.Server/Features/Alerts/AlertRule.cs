using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Alerts;

/// <summary>
/// "Alert when <see cref="Metric"/> stays <see cref="Operator"/> <see cref="Threshold"/> for
/// <see cref="DurationSeconds"/>." Applies to the owner's hosts: all of them, one host, or those with a tag.
/// Anomaly rules compare with each host's usual level instead of a fixed threshold.
/// </summary>
public sealed class AlertRule
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public ArgusUser? Owner { get; set; }

    public string Name { get; set; } = "";

    public AlertMetric Metric { get; set; }

    public AlertCondition Condition { get; set; }

    public AlertOperator Operator { get; set; }

    /// <summary>The metric's value, or for anomaly rules a number of standard deviations.</summary>
    public double Threshold { get; set; }

    /// <summary>How long the condition must hold before the alert fires (0 = on the latest sample).</summary>
    public int DurationSeconds { get; set; }

    public AlertSeverity Severity { get; set; }

    /// <summary>Limits the rule to one host.</summary>
    public Guid? HostId { get; set; }

    public MonitoredHost? Host { get; set; }

    /// <summary>Limits the rule to hosts carrying this tag.</summary>
    public string? Tag { get; set; }

    /// <summary>For filesystem metrics: only this mount point (all filesystems when empty).</summary>
    public string? ResourceFilter { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> rule)
    {
        rule.ToTable("alert_rules");

        rule.HasOne(r => r.Owner).WithMany().HasForeignKey(r => r.OwnerId).OnDelete(DeleteBehavior.Cascade);
        rule.HasOne(r => r.Host).WithMany().HasForeignKey(r => r.HostId).OnDelete(DeleteBehavior.Cascade);

        rule.Property(r => r.Name).HasMaxLength(100);
        rule.Property(r => r.Metric).HasConversion<string>().HasMaxLength(32);
        rule.Property(r => r.Condition).HasConversion<string>().HasMaxLength(16);
        rule.Property(r => r.Operator).HasConversion<string>().HasMaxLength(16);
        rule.Property(r => r.Severity).HasConversion<string>().HasMaxLength(16);
        rule.Property(r => r.Tag).HasMaxLength(HostTags.MaxLength);
        rule.Property(r => r.ResourceFilter).HasMaxLength(256);

        rule.HasIndex(r => r.OwnerId);
    }
}
