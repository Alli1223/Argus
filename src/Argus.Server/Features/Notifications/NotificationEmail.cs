using System.Globalization;
using System.Net;
using System.Text;
using Argus.Server.Features.Alerts;

namespace Argus.Server.Features.Notifications;

public sealed record EmailContent(string Subject, string Html, string Text);

/// <summary>Notification emails: a short subject, and the same content as HTML and as plain text.</summary>
public static class NotificationEmail
{
    private const string Ink = "#1d2136";
    private const string Muted = "#5c6178";

    public static EmailContent ForAlert(AlertNotification alert, string channelName, string? publicUrl)
    {
        var resolved = alert.Kind == AlertEventKind.Resolved;
        var subject = resolved ? $"Resolved: {alert.Title}" : $"[{alert.Severity}] {alert.Title}";
        var link = alert.HostLink(publicUrl);

        List<(string Label, string Value)> facts =
        [
            (resolved ? "Last reading" : "Reading", alert.Reading),
            ("Host", alert.HostName),
            ("Severity", alert.Severity.ToString()),
            ("Started", FormatTime(alert.FiredAt)),
        ];
        if (resolved && alert.ResolvedAt is { } resolvedAt)
        {
            facts.Add(("Resolved", $"{FormatTime(resolvedAt)}, after {AlertNotification.DescribeDuration(resolvedAt - alert.FiredAt)}"));
        }

        if (alert.RuleName is { } rule)
        {
            facts.Add(("Rule", rule));
        }

        var footer = $"You get this email because the notification channel \"{channelName}\" in Argus sends alerts to this address.";

        var text = new StringBuilder();
        text.AppendLine(resolved ? $"Resolved: {alert.Title}" : alert.Title).AppendLine();
        foreach (var (label, value) in facts)
        {
            text.AppendLine($"{label}: {value}");
        }

        if (link is not null)
        {
            text.AppendLine().AppendLine($"Open in Argus: {link}");
        }

        text.AppendLine().AppendLine(footer);

        // The web app's status colours, in shades dark enough for text on white.
        var accent = resolved
            ? "#0a6b53"
            : alert.Severity switch
            {
                AlertSeverity.Critical => "#ad2921",
                AlertSeverity.Warning => "#7e5507",
                _ => "#414cc7",
            };
        var rows = string.Concat(facts.Select(fact => $"""
            <tr>
              <td style="padding:6px 16px 6px 0;color:{Muted};white-space:nowrap;vertical-align:top">{Encode(fact.Label)}</td>
              <td style="padding:6px 0;color:{Ink}">{Encode(fact.Value)}</td>
            </tr>
            """));
        var button = link is null
            ? ""
            : $"""
              <p style="margin:24px 0 0"><a href="{Encode(link)}" style="display:inline-block;padding:10px 18px;border-radius:6px;background:#414cc7;color:#ffffff;text-decoration:none;font-weight:600">Open in Argus</a></p>
              """;
        var html = Layout(
            subject,
            accent,
            status: resolved ? "Resolved" : $"{alert.Severity} alert",
            heading: alert.Title,
            body: $"""<table role="presentation" cellpadding="0" cellspacing="0">{rows}</table>{button}""",
            footer);

        return new EmailContent(subject, html, text.ToString());
    }

    public static EmailContent ForTest(string channelName)
    {
        const string Subject = "Test from Argus";
        var message = $"The notification channel \"{channelName}\" works: alerts will arrive at this address.";
        var html = Layout(
            Subject,
            accent: "#414cc7",
            status: "Test",
            heading: Subject,
            body: $"""<p style="margin:0;color:{Ink}">{Encode(message)}</p>""",
            footer: "Someone sent this test from the channel's settings in Argus.");

        return new EmailContent(Subject, html, $"{Subject}\n\n{message}\n");
    }

    private static string FormatTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>A card with a coloured top edge. Every argument but <paramref name="body"/> is plain text.</summary>
    private static string Layout(string subject, string accent, string status, string heading, string body, string footer) => $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{Encode(subject)}</title>
        </head>
        <body style="margin:0;padding:24px 12px;background:#f4f5fa;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:14px;line-height:1.5">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:8px;border-top:4px solid {accent}">
            <tr>
              <td style="padding:24px">
                <p style="margin:0 0 4px;color:{accent};font-weight:600">{Encode(status)}</p>
                <h1 style="margin:0 0 16px;font-size:18px;line-height:1.3;color:{Ink}">{Encode(heading)}</h1>
                {body}
              </td>
            </tr>
          </table>
          <p style="max-width:560px;margin:16px auto 0;color:{Muted};font-size:12px">{Encode(footer)}</p>
        </body>
        </html>
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
