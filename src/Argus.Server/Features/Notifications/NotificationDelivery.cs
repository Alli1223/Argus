using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Argus.Server.Features.Notifications;

/// <summary>
/// One notification on its way to one channel. Together these form the queue the
/// <see cref="NotificationDispatcher"/> works through, retrying failed sends with growing gaps.
/// </summary>
public sealed class NotificationDelivery
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }

    public NotificationChannel? Channel { get; set; }

    public NotificationKind Kind { get; set; }

    /// <summary>The message as JSON, captured when it was queued (an <see cref="AlertNotification"/> for alerts).</summary>
    public string Payload { get; set; } = "";

    public DeliveryStatus Status { get; set; }

    public int Attempts { get; set; }

    /// <summary>When a pending delivery is next due; while one is being sent, when its claim runs out.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public const int MaxErrorLength = 1000;

    public void Configure(EntityTypeBuilder<NotificationDelivery> delivery)
    {
        delivery.ToTable("notification_deliveries");

        delivery.HasOne(d => d.Channel).WithMany().HasForeignKey(d => d.ChannelId).OnDelete(DeleteBehavior.Cascade);

        delivery.Property(d => d.Kind).HasConversion<string>().HasMaxLength(32);
        delivery.Property(d => d.Payload).HasColumnType("jsonb");
        delivery.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        delivery.Property(d => d.LastError).HasMaxLength(MaxErrorLength);

        delivery.HasIndex(d => d.NextAttemptAt).HasFilter("status = 'Pending'");
        delivery.HasIndex(d => d.ChannelId);
        delivery.HasIndex(d => d.CreatedAt);
    }
}
