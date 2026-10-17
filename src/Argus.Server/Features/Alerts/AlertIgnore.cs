using Argus.Server.Features.Auth;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Alerts;

/// <summary>
/// A rule that no longer watches one host (and, for per-resource rules, one mount point, service, container
/// or UPS), because someone chose to ignore its alert. The rule goes on watching everything else, and
/// removing the ignore starts watching again.
/// </summary>
public sealed class AlertIgnore
{
    public Guid Id { get; set; }

    public Guid RuleId { get; set; }

    public AlertRule? Rule { get; set; }

    public Guid HostId { get; set; }

    public MonitoredHost? Host { get; set; }

    /// <summary>The rule's owner, whose alerts these would have been.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>The resource as in <see cref="Alert.ResourceKey"/>; empty for host-wide rules.</summary>
    public string ResourceKey { get; set; } = "";

    /// <summary>The title of the alert that was ignored, to recognise the ignore by.</summary>
    public string Title { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedById { get; set; }
}

internal sealed class AlertIgnoreConfiguration : IEntityTypeConfiguration<AlertIgnore>
{
    public void Configure(EntityTypeBuilder<AlertIgnore> ignore)
    {
        ignore.ToTable("alert_ignores");

        ignore.HasOne(i => i.Rule).WithMany().HasForeignKey(i => i.RuleId).OnDelete(DeleteBehavior.Cascade);
        ignore.HasOne(i => i.Host).WithMany().HasForeignKey(i => i.HostId).OnDelete(DeleteBehavior.Cascade);
        ignore.HasOne<ArgusUser>().WithMany().HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.Cascade);
        ignore.HasOne<ArgusUser>().WithMany().HasForeignKey(i => i.CreatedById).OnDelete(DeleteBehavior.SetNull);

        ignore.Property(i => i.ResourceKey).HasMaxLength(256);
        ignore.Property(i => i.Title).HasMaxLength(300);

        ignore.HasIndex(i => new { i.RuleId, i.HostId, i.ResourceKey }).IsUnique();
        ignore.HasIndex(i => i.OwnerId);
    }
}
