using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Notifications;

/// <summary>
/// Somewhere a user's alerts are sent. A channel passes on alerts of at least
/// <see cref="MinimumSeverity"/> when they fire, and when they resolve if <see cref="NotifyOnResolved"/>.
/// </summary>
public sealed class NotificationChannel
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public ArgusUser? Owner { get; set; }

    public string Name { get; set; } = "";

    public NotificationChannelKind Kind { get; set; }

    /// <summary>For email: the addresses, separated by commas.</summary>
    public string Target { get; set; } = "";

    public AlertSeverity MinimumSeverity { get; set; } = AlertSeverity.Warning;

    public bool NotifyOnResolved { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Whether this channel passes on an alert of this severity firing or resolving.</summary>
    public bool Wants(AlertEventKind kind, AlertSeverity severity) =>
        Enabled && severity >= MinimumSeverity && (kind == AlertEventKind.Fired || NotifyOnResolved);
}

internal sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> channel)
    {
        channel.ToTable("notification_channels");

        channel.HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerId).OnDelete(DeleteBehavior.Cascade);

        channel.Property(c => c.Name).HasMaxLength(100);
        channel.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
        channel.Property(c => c.Target).HasMaxLength(2000);
        channel.Property(c => c.MinimumSeverity).HasConversion<string>().HasMaxLength(16);

        channel.HasIndex(c => c.OwnerId);
    }
}
