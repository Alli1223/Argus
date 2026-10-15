using Argus.Server.Features.Alerts;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Power;

public enum PowerActionKind
{
    Shutdown,
    Restart,
    Wake,
}

/// <summary>
/// One time Argus shut a host down, restarted it or woke it: who or which alert rule asked, and how it went.
/// </summary>
public sealed class PowerAction
{
    public Guid Id { get; set; }

    public Guid HostId { get; set; }

    public MonitoredHost? Host { get; set; }

    public PowerActionKind Kind { get; set; }

    /// <summary>The user who asked; null when an alert rule did.</summary>
    public string? RequestedBy { get; set; }

    /// <summary>The rule whose alert asked; null when a user did, or once the rule has been deleted.</summary>
    public Guid? RuleId { get; set; }

    public AlertRule? Rule { get; set; }

    /// <summary>Why, in words: the alert's title for rules.</summary>
    public string Reason { get; set; } = "";

    public bool Succeeded { get; set; }

    /// <summary>What went wrong, or for wakes which machines sent the packet.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset At { get; set; }
}

internal sealed class PowerActionConfiguration : IEntityTypeConfiguration<PowerAction>
{
    public const int ReasonLength = 300;
    public const int DetailLength = 1000;

    public void Configure(EntityTypeBuilder<PowerAction> action)
    {
        action.ToTable("power_actions");

        action.HasOne(a => a.Host).WithMany().HasForeignKey(a => a.HostId).OnDelete(DeleteBehavior.Cascade);
        action.HasOne(a => a.Rule).WithMany().HasForeignKey(a => a.RuleId).OnDelete(DeleteBehavior.SetNull);

        action.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
        action.Property(a => a.RequestedBy).HasMaxLength(256);
        action.Property(a => a.Reason).HasMaxLength(ReasonLength);
        action.Property(a => a.Detail).HasMaxLength(DetailLength);

        action.HasIndex(a => new { a.HostId, a.At });
    }
}
