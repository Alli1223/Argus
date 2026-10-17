using System.Globalization;
using Argus.Server.Data;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Notifications;
using Argus.Server.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Telegram;

/// <summary>
/// Answers what people send the bot: the chat ID for /start, and the buttons under alerts. A button only acts
/// on an alert whose owner has a Telegram channel for that chat, so a stranger's chat cannot reach anyone's
/// alerts. Anyone in that chat may press them, as anyone there can read the alerts.
/// </summary>
public sealed class TelegramUpdateHandler(
    ArgusDbContext db,
    AlertActions actions,
    TelegramApi telegram,
    IOptions<ArgusOptions> argus,
    ILogger<TelegramUpdateHandler> logger)
{
    public async Task HandleAsync(TelegramBot bot, TelegramUpdate update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } press)
        {
            await HandlePressAsync(bot, press, cancellationToken);
        }
        else if ((update.Message ?? update.ChannelPost) is { Text: { } text } message && AsksForChatId(bot, message, text))
        {
            await telegram.SendMessageAsync(
                bot.Token, message.Chat.Id.ToString(CultureInfo.InvariantCulture), TelegramMessages.ForChatId(message.Chat), null, cancellationToken);
        }
    }

    /// <summary>Private chats get the ID for anything; groups and channels only for /start or /chatid, to stay quiet.</summary>
    private static bool AsksForChatId(TelegramBot bot, TelegramMessage message, string text)
    {
        if (message.Chat.Type == "private")
        {
            return true;
        }

        var command = text.Split(' ', 2)[0];
        var at = command.IndexOf('@');
        if (at >= 0)
        {
            if (!command[(at + 1)..].Equals(bot.Username, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            command = command[..at];
        }

        return command is "/start" or "/chatid";
    }

    private async Task HandlePressAsync(TelegramBot bot, TelegramCallbackQuery press, CancellationToken cancellationToken)
    {
        var parts = (press.Data ?? "").Split(':', 2);
        if (press.Message is not { } message || parts.Length != 2 || !Guid.TryParse(parts[1], out var alertId))
        {
            await telegram.AnswerCallbackAsync(bot.Token, press.Id, "This button no longer works.", cancellationToken);
            return;
        }

        var chatId = message.Chat.Id.ToString(CultureInfo.InvariantCulture);
        var chatName = message.Chat.Username is { } username ? "@" + username : null;
        var owners = db.NotificationChannels
            .Where(channel => channel.Kind == NotificationChannelKind.Telegram && (channel.Target == chatId || channel.Target == chatName))
            .Select(channel => channel.OwnerId);
        var row = await db.Alerts
            .Where(alert => alert.Id == alertId && owners.Contains(alert.OwnerId))
            .Select(alert => new { Alert = alert, HostName = alert.Host!.DisplayName, RuleName = alert.Rule != null ? alert.Rule.Name : null })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            await telegram.AnswerCallbackAsync(bot.Token, press.Id, "This chat is no longer a notification channel for that alert.", cancellationToken);
            return;
        }

        var alert = row.Alert;
        var who = press.From.DisplayName;
        string? answer = null;
        string? footer = null;
        switch (parts[0])
        {
            case TelegramMessages.Acknowledge:
                var acknowledged = await actions.AcknowledgeAsync(alert, alert.OwnerId, cancellationToken);
                (answer, footer) = acknowledged switch
                {
                    AlertActionOutcome.Done => ("Acknowledged.", $"✅ Acknowledged by {who}"),
                    AlertActionOutcome.AlreadyDone => ("It was already acknowledged.", null),
                    _ => ("This alert has already resolved.", null),
                };
                break;

            case TelegramMessages.Ignore when alert.Status == AlertStatus.Firing:
                await telegram.AnswerCallbackAsync(bot.Token, press.Id, null, cancellationToken);
                await EditAsync(bot, message, row.HostName, row.RuleName, alert, TelegramMessages.ConfirmIgnoreQuestion,
                    TelegramMessages.ConfirmIgnoreButtons(alert.Id), cancellationToken);
                return;

            case TelegramMessages.ConfirmIgnore:
                var (ignored, _) = await actions.IgnoreAsync(alert, alert.OwnerId, cancellationToken);
                (answer, footer) = ignored switch
                {
                    AlertActionOutcome.NoRule => ("This alert's rule was deleted, so nothing is monitoring it.", null),
                    _ => ("No longer monitored.", $"🔕 Ignored by {who}: no longer monitored"),
                };
                if (ignored != AlertActionOutcome.NoRule)
                {
                    logger.LogInformation("Alert {AlertId} ignored from Telegram by {TelegramUser}", alert.Id, press.From.Id);
                }

                break;

            case TelegramMessages.Ignore or TelegramMessages.CancelIgnore:
                break;

            default:
                answer = "This button no longer works.";
                break;
        }

        await telegram.AnswerCallbackAsync(bot.Token, press.Id, answer, cancellationToken);
        await EditAsync(bot, message, row.HostName, row.RuleName, alert, footer, null, cancellationToken);
    }

    /// <summary>Redraws the alert's message as the alert now stands, with the buttons that still apply.</summary>
    private async Task EditAsync(
        TelegramBot bot,
        TelegramMessage message,
        string hostName,
        string? ruleName,
        Alert alert,
        string? footer,
        TelegramKeyboard? buttons,
        CancellationToken cancellationToken)
    {
        footer ??= alert switch
        {
            { IgnoredAt: not null } => "🔕 Ignored: no longer monitored",
            { Status: AlertStatus.Resolved } => "✅ Resolved",
            { AcknowledgedAt: not null } => "✅ Acknowledged",
            _ => null,
        };
        buttons ??= alert.Status == AlertStatus.Firing
            ? TelegramMessages.ButtonsFor(alert.Id, acknowledged: alert.AcknowledgedAt is not null)
            : TelegramKeyboard.None;

        var notification = AlertNotification.From(AlertEventKind.Fired, alert, hostName, ruleName);
        await telegram.EditMessageAsync(
            bot.Token, message.Chat.Id, message.MessageId, TelegramMessages.ForAlert(notification, argus.Value.PublicUrl, footer), buttons,
            cancellationToken);
    }
}
