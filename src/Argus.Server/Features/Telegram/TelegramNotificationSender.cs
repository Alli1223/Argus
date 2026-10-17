using Argus.Server.Features.Notifications;
using Argus.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Telegram;

/// <summary>Sends notifications to a Telegram chat through the server's bot. Firing alerts come with buttons.</summary>
internal sealed class TelegramNotificationSender(TelegramApi telegram, TelegramSettingsStore settings, IOptions<ArgusOptions> argus)
    : INotificationSender
{
    public NotificationChannelKind Kind => NotificationChannelKind.Telegram;

    public async Task SendAsync(NotificationChannel channel, NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var bot = await settings.CurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException("Telegram is not set up on this server: an administrator adds a bot on the Settings page.");

        var publicUrl = argus.Value.PublicUrl;
        var (text, buttons) = delivery.Kind switch
        {
            NotificationKind.AlertFired or NotificationKind.AlertResolved => ForAlert(AlertNotification.FromJson(delivery.Payload), publicUrl),
            NotificationKind.Test => (TelegramMessages.ForTest(channel.Name), null),
            NotificationKind.DailyReport or NotificationKind.WeeklyReport =>
                (TelegramMessages.ForReport(ReportSummary.FromJson(delivery.Payload), publicUrl), null),
            _ => throw new NotSupportedException($"There is no Telegram message for {delivery.Kind} notifications."),
        };

        try
        {
            await telegram.SendMessageAsync(bot.Token, channel.Target, text, buttons, cancellationToken);
        }
        catch (TelegramApiException ex) when (ex.ErrorCode is 400 or 403)
        {
            // The usual reasons: nobody has started a chat with the bot, or it is not in the group.
            throw new TelegramApiException(
                $"{ex.Message} Check the chat ID, and that the chat has sent /start to @{bot.Username} (or added it to the group).",
                ex.ErrorCode);
        }
    }

    private static (string Text, TelegramKeyboard? Buttons) ForAlert(AlertNotification alert, string? publicUrl) =>
        alert.Kind == Alerts.AlertEventKind.Fired
            ? (TelegramMessages.ForAlert(alert, publicUrl), TelegramMessages.ButtonsFor(alert.AlertId, acknowledged: false))
            : (TelegramMessages.ForAlert(alert, publicUrl), null);
}
