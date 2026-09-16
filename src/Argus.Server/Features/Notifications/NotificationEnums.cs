namespace Argus.Server.Features.Notifications;

/// <summary>Where a channel sends notifications.</summary>
public enum NotificationChannelKind
{
    /// <summary>One or more email addresses, reached through the server's SMTP settings.</summary>
    Email,

    /// <summary>A URL that receives each notification as Argus's own JSON.</summary>
    Webhook,

    /// <summary>A Slack incoming webhook URL.</summary>
    Slack,

    /// <summary>A Discord channel webhook URL.</summary>
    Discord,
}

/// <summary>What a notification is about.</summary>
public enum NotificationKind
{
    AlertFired,
    AlertResolved,
}

public enum DeliveryStatus
{
    /// <summary>Waiting to be sent, or to be tried again.</summary>
    Pending,

    Sent,

    /// <summary>Given up on: the last attempt failed, or the channel can no longer take it.</summary>
    Failed,
}
