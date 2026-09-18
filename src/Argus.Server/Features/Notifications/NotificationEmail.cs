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
    private const string Rule = "#e4e6f0";

    // The web app's status colours, in shades dark enough for text on white.
    private const string Healthy = "#0a6b53";

    private static string SeverityAccent(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "#ad2921",
        AlertSeverity.Warning => "#7e5507",
        _ => "#414cc7",
    };

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
        AppendFacts(text, facts);
        AppendLink(text, "Open in Argus", link);
        text.AppendLine().AppendLine(footer);

        var html = Layout(
            subject,
            accent: resolved ? Healthy : SeverityAccent(alert.Severity),
            status: resolved ? "Resolved" : $"{alert.Severity} alert",
            heading: alert.Title,
            body: FactsTable(facts) + Button("Open in Argus", link),
            footer);

        return new EmailContent(subject, html, text.ToString());
    }

    /// <summary>The test an administrator sends from the email settings, before any channel exists.</summary>
    public static EmailContent ForServerTest()
    {
        const string Subject = "Test from Argus";
        const string Message = "Argus can send email: alerts will arrive at this address.";
        var html = Layout(
            Subject,
            accent: "#414cc7",
            status: "Test",
            heading: Subject,
            body: $"""<p style="margin:0;color:{Ink}">{Encode(Message)}</p>""",
            footer: "Someone sent this test from the email settings in Argus.");

        return new EmailContent(Subject, html, $"{Subject}\n\n{Message}\n");
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

    public static EmailContent ForReport(ReportSummary report, string channelName, string? publicUrl)
    {
        var subject = ReportText.Title(report);
        var headline = ReportText.Headline(report);
        var link = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.TrimEnd('/');
        var daily = report.Kind == ReportKind.Daily;

        List<(string Label, string Value)> facts =
        [
            ("Period", ReportText.Period(report)),
            ("Hosts", ReportText.Hosts(report)),
            ("Alerts raised", ReportText.Raised(report.AlertsRaised)),
            ("Firing now", report.FiringCount.ToString(CultureInfo.InvariantCulture)),
        ];
        var firing = report.Firing.Select(alert => $"[{alert.Severity}] {alert.Title}, since {ReportText.Time(alert.FiredAt)}").ToList();
        if (report.FiringCount > report.Firing.Count)
        {
            firing.Add($"and {report.FiringCount - report.Firing.Count} more");
        }

        var failures = report.FailedServices
            .Select(failure => $"{failure.Service} on {failure.HostName}, since {ReportText.Time(failure.Since)}")
            .ToList();
        var footer = $"You get this report because the notification channel \"{channelName}\" in Argus sends {(daily ? "daily" : "weekly")} reports here.";

        var text = new StringBuilder();
        text.AppendLine(subject).AppendLine(headline).AppendLine();
        AppendFacts(text, facts);
        AppendList(text, "Firing now", firing);
        AppendList(text, "Failed services", failures);
        AppendList(text, "Hosts", report.Hosts.Select(host =>
            $"{host.Name}: {(host.Online ? "online" : "offline")}, CPU {ReportText.Percent(host.CpuAverage)} on average " +
            $"({ReportText.Percent(host.CpuPeak)} at most), memory {ReportText.Percent(host.MemoryAverage)}, " +
            $"fullest disk {ReportText.Percent(host.FullestDiskPercent)}, {AlertCount(host.AlertsRaised)}").ToList());
        AppendLink(text, "Open Argus", link);
        text.AppendLine().AppendLine(footer);

        var body = new StringBuilder(FactsTable(facts));
        body.Append(ListSection("Firing now", firing));
        body.Append(ListSection("Failed services", failures));
        if (report.Hosts.Count > 0)
        {
            body.Append(SectionHeading("Hosts")).Append(HostTable(report.Hosts));
        }

        body.Append(Button("Open Argus", link));

        var html = Layout(
            subject,
            accent: report.Firing.Count == 0 ? Healthy : SeverityAccent(report.Firing[0].Severity),
            status: daily ? "Daily report" : "Weekly report",
            heading: headline,
            body: body.ToString(),
            footer);

        return new EmailContent(subject, html, text.ToString());
    }

    private static string FormatTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string AlertCount(int count) => count == 1 ? "1 alert" : $"{count} alerts";

    private static void AppendFacts(StringBuilder text, List<(string Label, string Value)> facts)
    {
        foreach (var (label, value) in facts)
        {
            text.AppendLine($"{label}: {value}");
        }
    }

    private static void AppendList(StringBuilder text, string heading, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        text.AppendLine().AppendLine(heading);
        foreach (var item in items)
        {
            text.AppendLine($"- {item}");
        }
    }

    private static void AppendLink(StringBuilder text, string label, string? link)
    {
        if (link is not null)
        {
            text.AppendLine().AppendLine($"{label}: {link}");
        }
    }

    private static string FactsTable(List<(string Label, string Value)> facts) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0">{string.Concat(facts.Select(fact => $"""
            <tr>
              <td style="padding:6px 16px 6px 0;color:{Muted};white-space:nowrap;vertical-align:top">{Encode(fact.Label)}</td>
              <td style="padding:6px 0;color:{Ink}">{Encode(fact.Value)}</td>
            </tr>
            """))}</table>
        """;

    private static string SectionHeading(string heading) =>
        $"""<h2 style="margin:24px 0 8px;font-size:15px;color:{Ink}">{Encode(heading)}</h2>""";

    private static string ListSection(string heading, List<string> items) => items.Count == 0
        ? ""
        : SectionHeading(heading) + $"""
            <ul style="margin:0;padding:0 0 0 18px;color:{Ink}">{string.Concat(items.Select(item => $"""<li style="margin:0 0 4px">{Encode(item)}</li>"""))}</ul>
            """;

    private static string HostTable(IReadOnlyList<ReportHost> hosts)
    {
        const string Head = "padding:6px 8px 6px 0;color:" + Muted + ";font-weight:600;text-align:left;border-bottom:1px solid " + Rule;
        const string Cell = "padding:6px 8px 6px 0;color:" + Ink + ";border-bottom:1px solid " + Rule;
        var rows = string.Concat(hosts.Select(host => $"""
            <tr>
              <td style="{Cell}">{Encode(host.Name)}</td>
              <td style="{Cell};color:{(host.Online ? Ink : SeverityAccent(AlertSeverity.Critical))}">{(host.Online ? "Online" : "Offline")}</td>
              <td style="{Cell};text-align:right">{ReportText.Percent(host.CpuAverage)}</td>
              <td style="{Cell};text-align:right">{ReportText.Percent(host.CpuPeak)}</td>
              <td style="{Cell};text-align:right">{ReportText.Percent(host.MemoryAverage)}</td>
              <td style="{Cell};text-align:right">{ReportText.Percent(host.FullestDiskPercent)}</td>
              <td style="{Cell};text-align:right">{host.AlertsRaised}</td>
            </tr>
            """));
        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;border-collapse:collapse;font-size:13px">
              <tr>
                <th style="{Head}">Host</th>
                <th style="{Head}">Status</th>
                <th style="{Head};text-align:right">CPU average</th>
                <th style="{Head};text-align:right">CPU peak</th>
                <th style="{Head};text-align:right">Memory</th>
                <th style="{Head};text-align:right">Fullest disk</th>
                <th style="{Head};text-align:right">Alerts</th>
              </tr>
              {rows}
            </table>
            """;
    }

    private static string Button(string label, string? link) => link is null
        ? ""
        : $"""
          <p style="margin:24px 0 0"><a href="{Encode(link)}" style="display:inline-block;padding:10px 18px;border-radius:6px;background:#414cc7;color:#ffffff;text-decoration:none;font-weight:600">{Encode(label)}</a></p>
          """;

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
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;margin:0 auto;background:#ffffff;border-radius:8px;border-top:4px solid {accent}">
            <tr>
              <td style="padding:24px">
                <p style="margin:0 0 4px;color:{accent};font-weight:600">{Encode(status)}</p>
                <h1 style="margin:0 0 16px;font-size:18px;line-height:1.3;color:{Ink}">{Encode(heading)}</h1>
                {body}
              </td>
            </tr>
          </table>
          <p style="max-width:600px;margin:16px auto 0;color:{Muted};font-size:12px">{Encode(footer)}</p>
        </body>
        </html>
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
