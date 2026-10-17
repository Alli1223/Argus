using System.Text;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Notifications;

namespace Argus.Server.Features.Telegram;

/// <summary>What Argus says in Telegram, as the HTML subset the Bot API takes, and the buttons under it.</summary>
public static class TelegramMessages
{
    public const string Acknowledge = "ack";
    public const string Ignore = "ign";
    public const string ConfirmIgnore = "ign!";
    public const string CancelIgnore = "back";

    public static string ForAlert(AlertNotification alert, string? publicUrl, string? footer = null)
    {
        var text = new StringBuilder();
        var heading = alert.Kind == AlertEventKind.Resolved
            ? $"✅ Resolved: {alert.Title}"
            : $"{Emoji(alert.Severity)} [{alert.Severity}] {alert.Title}";
        text.Append("<b>").Append(Escape(heading)).Append("</b>\n");
        text.Append(Escape(alert.Reading)).Append('\n');
        text.Append("Host: ").Append(Escape(alert.HostName));
        if (alert.RuleName is { } rule)
        {
            text.Append("\nRule: ").Append(Escape(rule));
        }

        if (alert.HostLink(publicUrl) is { } link)
        {
            text.Append("\n<a href=\"").Append(Escape(link)).Append("\">Open in Argus</a>");
        }

        if (footer is not null)
        {
            text.Append("\n\n").Append(Escape(footer));
        }

        return text.ToString();
    }

    /// <summary>The buttons under a firing alert: what can still be done to it.</summary>
    public static TelegramKeyboard ButtonsFor(Guid alertId, bool acknowledged)
    {
        var ignore = new InlineButton("🔕 Ignore", $"{Ignore}:{alertId}");
        return acknowledged
            ? new TelegramKeyboard([[ignore]])
            : new TelegramKeyboard([[new InlineButton("✅ Acknowledge", $"{Acknowledge}:{alertId}"), ignore]]);
    }

    /// <summary>Ignoring stops monitoring, so it is asked about once more before it happens.</summary>
    public static TelegramKeyboard ConfirmIgnoreButtons(Guid alertId) => new(
    [
        [new InlineButton("🔕 Yes, stop monitoring", $"{ConfirmIgnore}:{alertId}")],
        [new InlineButton("Cancel", $"{CancelIgnore}:{alertId}")],
    ]);

    public const string ConfirmIgnoreQuestion =
        "Stop monitoring this? The rule will no longer check it, and the alert closes without a resolved message. "
        + "It can be monitored again from the Alerts page in Argus.";

    public static string ForTest(string channelName) =>
        $"<b>Test from Argus</b>\nThe notification channel \"{Escape(channelName)}\" works: alerts will arrive here.";

    public static string ForReport(ReportSummary report, string? publicUrl)
    {
        var text = new StringBuilder();
        text.Append("<b>").Append(Escape($"{ReportText.Title(report)}: {ReportText.Headline(report)}")).Append("</b>\n");
        text.Append(Escape($"Hosts: {ReportText.Hosts(report)}")).Append('\n');
        text.Append(Escape($"Alerts raised: {ReportText.Raised(report.AlertsRaised)}")).Append('\n');
        text.Append(Escape($"Firing now: {report.FiringCount}"));
        foreach (var alert in report.Firing.Take(5))
        {
            text.Append('\n').Append(Escape($"• [{alert.Severity}] {alert.Title}"));
        }

        if (report.FiringCount > 5)
        {
            text.Append('\n').Append(Escape($"• and {report.FiringCount - 5} more"));
        }

        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            text.Append("\n<a href=\"").Append(Escape(publicUrl.TrimEnd('/'))).Append("\">Open Argus</a>");
        }

        return text.ToString();
    }

    /// <summary>The bot's answer to /start: the ID to enter in Argus.</summary>
    public static string ForChatId(TelegramChat chat) =>
        $"Hello! This chat's ID is <code>{chat.Id}</code>.\n\n"
        + "To get alerts here, open <b>Notifications</b> in Argus, add a channel, choose <b>Telegram</b> and enter this ID.";

    /// <summary>The characters Telegram's HTML reads as markup; everything else, emoji included, stays as it is.</summary>
    public static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string Emoji(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "🔴",
        AlertSeverity.Warning => "🟠",
        _ => "🔵",
    };
}
