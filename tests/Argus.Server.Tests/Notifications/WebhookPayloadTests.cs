using System.Text.Json.Nodes;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Notifications;

namespace Argus.Server.Tests.Notifications;

public sealed class WebhookPayloadTests
{
    private static readonly AlertNotification Fired = new(
        AlertEventKind.Fired,
        AlertId: Guid.Parse("01a0aa6d-8818-7184-8117-044c3a6aaf73"),
        HostId: Guid.Parse("01a0aa6d-864f-7c6e-8840-23474b485dd1"),
        HostName: "db-1",
        Title: "Disk usage on db-1 /var <data> above 90%",
        AlertSeverity.Critical,
        Reading: "93.2%, threshold 90%",
        RuleName: "Disks & more",
        FiredAt: new DateTimeOffset(2026, 9, 16, 9, 5, 0, TimeSpan.Zero),
        ResolvedAt: null);

    private const string PublicUrl = "https://argus.example.com";

    private static string HostLink => $"{PublicUrl}/hosts/{Fired.HostId}";

    [Fact]
    public void Generic_webhooks_get_every_fact_as_plain_json()
    {
        var payload = WebhookPayloads.ForAlert(NotificationChannelKind.Webhook, Fired, PublicUrl);

        Assert.Equal("alert.fired", (string?)payload["event"]);
        var alert = payload["alert"]!;
        Assert.Equal(Fired.Title, (string?)alert["title"]);
        Assert.Equal("Critical", (string?)alert["severity"]);
        Assert.Equal("firing", (string?)alert["status"]);
        Assert.Equal("Disks & more", (string?)alert["rule"]);
        Assert.Equal(HostLink, (string?)alert["url"]);
        Assert.Null(alert["resolvedAt"]);
        Assert.Contains("\"firedAt\":\"2026-09-16T09:05:00+00:00\"", payload.ToJsonString());
    }

    [Fact]
    public void Slack_messages_link_the_heading_and_escape_markup()
    {
        var payload = WebhookPayloads.ForAlert(NotificationChannelKind.Slack, Fired, PublicUrl);

        Assert.Equal($"*<{HostLink}|[Critical] Disk usage on db-1 /var &lt;data&gt; above 90%>*", (string?)payload["text"]);
        var attachment = payload["attachments"]![0]!;
        Assert.Equal("#d0342a", (string?)attachment["color"]);
        Assert.Equal("Disks &amp; more", (string?)attachment["fields"]![2]!["value"]);
    }

    [Fact]
    public void Discord_messages_use_an_embed_coloured_by_status()
    {
        var resolved = Fired with { Kind = AlertEventKind.Resolved, ResolvedAt = Fired.FiredAt.AddMinutes(20) };

        var embed = WebhookPayloads.ForAlert(NotificationChannelKind.Discord, resolved, publicUrl: null)["embeds"]![0]!;

        Assert.Equal("Resolved: Disk usage on db-1 /var <data> above 90%", (string?)embed["title"]);
        Assert.Equal(0x13a17b, (int?)embed["color"]);
        Assert.Null(embed["url"]);
        Assert.Equal(resolved.ResolvedAt, embed["timestamp"]!.GetValue<DateTimeOffset>());
        Assert.Equal(["Reading", "Host", "Rule"], embed["fields"]!.AsArray().Select(field => (string?)field!["name"]));
    }
}
