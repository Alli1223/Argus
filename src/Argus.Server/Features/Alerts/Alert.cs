using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Alerts;

/// <summary>
/// One occurrence of a rule's condition on a host (and, for filesystem rules, a mount point).
/// It is <see cref="AlertStatus.Firing"/> until the condition clears, then <see cref="AlertStatus.Resolved"/>.
/// The rule's settings are copied in, so the history still reads correctly after the rule changes.
/// </summary>
public sealed class Alert
{
    public Guid Id { get; set; }

    /// <summary>Null once the rule has been deleted.</summary>
    public Guid? RuleId { get; set; }

    public AlertRule? Rule { get; set; }

    public Guid HostId { get; set; }

    public MonitoredHost? Host { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>The mount point for filesystem rules; empty otherwise.</summary>
    public string ResourceKey { get; set; } = "";

    public string Title { get; set; } = "";

    public AlertMetric Metric { get; set; }

    public AlertCondition Condition { get; set; }

    public AlertOperator Operator { get; set; }

    public double Threshold { get; set; }

    public AlertSeverity Severity { get; set; }

    public AlertStatus Status { get; set; }

    /// <summary>The observed value: the average over the rule's window, refreshed while firing.</summary>
    public double? Value { get; set; }

    /// <summary>For anomaly alerts: the host's usual level, which the value strayed from.</summary>
    public double? Baseline { get; set; }

    public DateTimeOffset FiredAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    public Guid? AcknowledgedById { get; set; }
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> alert)
    {
        alert.ToTable("alerts");

        alert.HasOne(a => a.Rule).WithMany().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.SetNull);
        alert.HasOne(a => a.Host).WithMany().HasForeignKey(a => a.HostId).OnDelete(DeleteBehavior.Cascade);
        alert.HasOne<ArgusUser>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Cascade);
        alert.HasOne<ArgusUser>().WithMany().HasForeignKey(a => a.AcknowledgedById).OnDelete(DeleteBehavior.SetNull);

        alert.Property(a => a.ResourceKey).HasMaxLength(256);
        alert.Property(a => a.Title).HasMaxLength(300);
        alert.Property(a => a.Metric).HasConversion<string>().HasMaxLength(32);
        alert.Property(a => a.Condition).HasConversion<string>().HasMaxLength(16);
        alert.Property(a => a.Operator).HasConversion<string>().HasMaxLength(16);
        alert.Property(a => a.Severity).HasConversion<string>().HasMaxLength(16);
        alert.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);

        // At most one open alert per rule, host and resource; the database enforces it.
        alert.HasIndex(a => new { a.RuleId, a.HostId, a.ResourceKey })
            .IsUnique()
            .HasFilter("status = 'Firing'");
        alert.HasIndex(a => new { a.OwnerId, a.Status, a.FiredAt });
        alert.HasIndex(a => a.HostId);
    }
}
