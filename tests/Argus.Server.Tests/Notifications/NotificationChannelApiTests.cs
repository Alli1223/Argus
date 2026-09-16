using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Notifications;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Argus.Server.Tests.Notifications;

public sealed class NotificationChannelApiTests(NotificationsFixture app) : IClassFixture<NotificationsFixture>
{
    private const string Route = "/api/notification-channels";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<NotificationChannelResponse> CreateAsync(HttpClient owner, object request)
    {
        var response = await owner.PostAsJsonAsync(Route, request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationChannelResponse>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Channels_are_created_updated_and_deleted()
    {
        var owner = await app.CreateOwnerAsync("channels-a@example.com");

        var channel = await CreateAsync(owner, new { name = " Ops ", kind = "Email", target = " ops@example.com;oncall@example.com " });
        Assert.Equal("Ops", channel.Name);
        Assert.Equal("ops@example.com, oncall@example.com", channel.Target);
        Assert.Equal(AlertSeverity.Warning, channel.MinimumSeverity);
        Assert.True(channel.NotifyOnResolved && channel.Enabled);
        Assert.Null(channel.LastDelivery);

        var updated = await owner.PutAsJsonAsync($"{Route}/{channel.Id}", new
        {
            name = "Chat",
            kind = "Slack",
            target = "https://hooks.slack.com/services/T0/B0/x",
            minimumSeverity = "Critical",
            notifyOnResolved = false,
            enabled = false,
        }, Ct);
        updated.EnsureSuccessStatusCode();
        var changed = (await updated.Content.ReadFromJsonAsync<NotificationChannelResponse>(TestJson.Options, Ct))!;
        Assert.Equal((NotificationChannelKind.Slack, AlertSeverity.Critical, false, false),
            (changed.Kind, changed.MinimumSeverity, changed.NotifyOnResolved, changed.Enabled));
        Assert.Equal([changed.Id], (await owner.GetJsonAsync<List<NotificationChannelResponse>>(Route))!.Select(c => c.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Route}/{channel.Id}", Ct)).StatusCode);
        Assert.Empty((await owner.GetJsonAsync<List<NotificationChannelResponse>>(Route))!);
    }

    [Fact]
    public async Task Targets_must_suit_the_kind_of_channel()
    {
        var owner = await app.CreateOwnerAsync("channels-b@example.com");

        async Task<IDictionary<string, string[]>> ErrorsFor(object request)
        {
            var response = await owner.PostAsJsonAsync(Route, request, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors;
        }

        Assert.Contains("target", (await ErrorsFor(new { name = "x", kind = "Email", target = "ops@example.com, not-an-address" })).Keys);
        Assert.Contains("target", (await ErrorsFor(new { name = "x", kind = "Email", target = " , " })).Keys);
        var tooMany = string.Join(",", Enumerable.Range(0, NotificationChannelEndpoints.MaxEmailAddresses + 1).Select(i => $"o{i}@example.com"));
        Assert.Contains("target", (await ErrorsFor(new { name = "x", kind = "Email", target = tooMany })).Keys);
        Assert.Contains("target", (await ErrorsFor(new { name = "x", kind = "Webhook", target = "ftp://example.com/hook" })).Keys);
        Assert.Contains("target", (await ErrorsFor(new { name = "x", kind = "Discord", target = "discord.com/api/webhooks/1" })).Keys);
        Assert.Contains("name", (await ErrorsFor(new { name = "  ", kind = "Webhook", target = "https://example.com/hook" })).Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("kind", (await ErrorsFor(new { name = "x", target = "https://example.com/hook" })).Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Channels_belong_to_one_person()
    {
        var owner = await app.CreateOwnerAsync("channels-c@example.com");
        var stranger = await app.CreateOwnerAsync("channels-d@example.com");
        var channel = await CreateAsync(owner, new { name = "Ops", kind = "Email", target = "ops-c@example.com" });

        Assert.Empty((await stranger.GetJsonAsync<List<NotificationChannelResponse>>(Route))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"{Route}/{channel.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"{Route}/{channel.Id}/test", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"{Route}/{channel.Id}", Ct)).StatusCode);
        Assert.Empty(app.Email.SentTo("ops-c@example.com"));
    }

    [Fact]
    public async Task Tests_are_sent_straight_away_and_report_failures()
    {
        const string HookUrl = "https://hooks.example.com/argus-e";
        var owner = await app.CreateOwnerAsync("channels-e@example.com");
        var email = await CreateAsync(owner, new { name = "Ops", kind = "Email", target = "ops-e@example.com", enabled = false });
        var hook = await CreateAsync(owner, new { name = "Hook", kind = "Webhook", target = HookUrl });

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{Route}/{email.Id}/test", null, Ct)).StatusCode);
        Assert.Equal("Test from Argus", Assert.Single(app.Email.SentTo("ops-e@example.com")).Subject);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"{Route}/{hook.Id}/test", null, Ct)).StatusCode);
        Assert.Equal("test", (string?)JsonNode.Parse(Assert.Single(app.Webhooks.BodiesSentTo(HookUrl)))!["event"]);

        app.Webhooks.Status = HttpStatusCode.InternalServerError;
        try
        {
            var failed = await owner.PostAsync($"{Route}/{hook.Id}/test", null, Ct);
            Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
            var problem = (await failed.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
            Assert.Equal("The webhook answered 500 Internal Server Error.", problem.Detail);
        }
        finally
        {
            app.Webhooks.Status = HttpStatusCode.NoContent;
        }

        // Tests go out directly; nothing is queued.
        Assert.Null((await owner.GetJsonAsync<NotificationChannelResponse>($"{Route}/{hook.Id}"))!.LastDelivery);
    }

    [Fact]
    public async Task Channels_show_how_their_latest_notification_went()
    {
        var owner = await app.CreateOwnerAsync("channels-f@example.com");
        var ownerId = (await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id;
        var (_, agent) = await app.RegisterHostAsync(owner, "channels-f-1");
        var channel = await CreateAsync(owner, new { name = "Ops", kind = "Email", target = "ops-f@example.com" });
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80);

        await agent.SendSamplesAsync(MetricsIngestionTests.FullSample(app.Time.GetUtcNow().AddSeconds(-5)) with
        {
            Cpu = new CpuMetrics { UsagePercent = 97 },
        });
        await app.EvaluateAsync();
        await app.DispatchAsync();

        var last = (await owner.GetJsonAsync<NotificationChannelResponse>($"{Route}/{channel.Id}"))!.LastDelivery!;
        Assert.Equal((DeliveryStatus.Sent, 1, app.Time.GetUtcNow()), (last.Status, last.Attempts, last.SentAt));
    }

    [Fact]
    public async Task The_server_says_whether_it_can_send_email_and_when_reports_go_out()
    {
        var owner = await app.CreateOwnerAsync("channels-g@example.com");

        Assert.Equal(new NotificationSupport(Email: true, ReportHourUtc: 7, WeeklyReportDay: DayOfWeek.Monday),
            await owner.GetJsonAsync<NotificationSupport>($"{Route}/support"));
    }
}
