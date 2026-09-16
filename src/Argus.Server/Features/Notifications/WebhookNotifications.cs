using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Argus.Server.Features.Alerts;
using Argus.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Notifications;

/// <summary>Posts notifications to a webhook URL, in the format of the channel's kind.</summary>
internal sealed class WebhookNotificationSender(NotificationChannelKind kind, IHttpClientFactory clients, IOptions<ArgusOptions> argus)
    : INotificationSender
{
    public const string HttpClientName = "notifications";

    public static readonly NotificationChannelKind[] Kinds =
        [NotificationChannelKind.Webhook, NotificationChannelKind.Slack, NotificationChannelKind.Discord];

    public NotificationChannelKind Kind => kind;

    public async Task SendAsync(NotificationChannel channel, NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        var payload = delivery.Kind switch
        {
            NotificationKind.AlertFired or NotificationKind.AlertResolved =>
                WebhookPayloads.ForAlert(kind, AlertNotification.FromJson(delivery.Payload), argus.Value.PublicUrl),
            _ => throw new NotSupportedException($"There is no webhook message for {delivery.Kind} notifications."),
        };

        using var response = await clients.CreateClient(HttpClientName)
            .PostAsync(channel.Target, JsonContent.Create(payload), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The webhook answered {(int)response.StatusCode} {response.ReasonPhrase}.", inner: null, response.StatusCode);
        }
    }
}

/// <summary>The JSON each kind of webhook channel receives.</summary>
public static class WebhookPayloads
{
    public static JsonObject ForAlert(NotificationChannelKind kind, AlertNotification alert, string? publicUrl)
    {
        var link = alert.HostLink(publicUrl);
        return kind switch
        {
            NotificationChannelKind.Slack => Slack(alert, link),
            NotificationChannelKind.Discord => Discord(alert, link),
            _ => Generic(alert, link),
        };
    }

    /// <summary>Argus's own format, for scripts and other tools.</summary>
    private static JsonObject Generic(AlertNotification alert, string? link) => new()
    {
        ["event"] = alert.Kind == AlertEventKind.Fired ? "alert.fired" : "alert.resolved",
        ["alert"] = new JsonObject
        {
            ["id"] = alert.AlertId,
            ["title"] = alert.Title,
            ["severity"] = alert.Severity.ToString(),
            ["status"] = alert.Kind == AlertEventKind.Fired ? "firing" : "resolved",
            ["reading"] = alert.Reading,
            ["rule"] = alert.RuleName,
            ["hostId"] = alert.HostId,
            ["hostName"] = alert.HostName,
            ["firedAt"] = alert.FiredAt,
            ["resolvedAt"] = alert.ResolvedAt,
            ["url"] = link,
        },
    };

    /// <summary>A Slack incoming webhook message: the heading, then a bar in the status colour with the facts.</summary>
    private static JsonObject Slack(AlertNotification alert, string? link)
    {
        var heading = SlackEscape(Heading(alert));
        var fields = new JsonArray(SlackField("Reading", alert.Reading), SlackField("Host", alert.HostName));
        if (alert.RuleName is { } rule)
        {
            fields.Add(SlackField("Rule", rule));
        }

        return new JsonObject
        {
            ["text"] = link is null ? $"*{heading}*" : $"*<{link}|{heading}>*",
            ["attachments"] = new JsonArray(new JsonObject
            {
                ["color"] = Colour(alert),
                ["fields"] = fields,
                ["footer"] = "Argus",
                ["ts"] = Moment(alert).ToUnixTimeSeconds(),
            }),
        };
    }

    /// <summary>A Discord webhook message with one embed in the status colour.</summary>
    private static JsonObject Discord(AlertNotification alert, string? link)
    {
        var fields = new JsonArray(DiscordField("Reading", alert.Reading), DiscordField("Host", alert.HostName));
        if (alert.RuleName is { } rule)
        {
            fields.Add(DiscordField("Rule", rule));
        }

        var embed = new JsonObject
        {
            ["title"] = Clip(Heading(alert), 256),
            ["color"] = Convert.ToInt32(Colour(alert)[1..], 16),
            ["fields"] = fields,
            ["footer"] = new JsonObject { ["text"] = "Argus" },
            ["timestamp"] = Moment(alert),
        };
        if (link is not null)
        {
            embed["url"] = link;
        }

        return new JsonObject { ["username"] = "Argus", ["embeds"] = new JsonArray(embed) };
    }

    private static string Heading(AlertNotification alert) =>
        alert.Kind == AlertEventKind.Resolved ? $"Resolved: {alert.Title}" : $"[{alert.Severity}] {alert.Title}";

    private static DateTimeOffset Moment(AlertNotification alert) =>
        alert.Kind == AlertEventKind.Resolved ? alert.ResolvedAt ?? alert.FiredAt : alert.FiredAt;

    /// <summary>The web app's status colours: green once resolved, otherwise the severity's.</summary>
    private static string Colour(AlertNotification alert) => alert.Kind == AlertEventKind.Resolved
        ? "#13a17b"
        : alert.Severity switch
        {
            AlertSeverity.Critical => "#d0342a",
            AlertSeverity.Warning => "#c2860c",
            _ => "#4f5be0",
        };

    private static JsonObject SlackField(string title, string value) =>
        new() { ["title"] = title, ["value"] = SlackEscape(value), ["short"] = true };

    private static JsonObject DiscordField(string name, string value) =>
        new() { ["name"] = name, ["value"] = Clip(value, 1024), ["inline"] = true };

    /// <summary>Slack reads &amp;, &lt; and &gt; as markup, so they are escaped.</summary>
    private static string SlackEscape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
