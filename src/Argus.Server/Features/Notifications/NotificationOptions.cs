using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Argus:Notifications";

    /// <summary>Send notifications in the background (tests switch this off and dispatch on demand).</summary>
    public bool BackgroundDelivery { get; set; } = true;

    /// <summary>How often the queue is checked for retries. New notifications go out straight away.</summary>
    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>How many times a notification is tried before it is given up on.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>How long sent and failed notifications are kept.</summary>
    [Range(1, 3650)]
    public int KeepDays { get; set; } = 30;
}
