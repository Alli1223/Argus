using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Notifications;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using MimeKit;

namespace Argus.Server.Tests.Notifications;

public sealed class ReportTests(NotificationsFixture app) : IClassFixture<NotificationsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => app.Time.GetUtcNow();

    private static bool IsDailyReport(MimeMessage message) => message.Subject?.StartsWith("Daily report", StringComparison.Ordinal) == true;

    private async Task<(Guid OwnerId, HttpClient Owner)> OwnerAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        return ((await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id, owner);
    }

    [Fact]
    public async Task Reports_sum_up_a_persons_hosts_once_per_period()
    {
        var (ownerId, owner) = await OwnerAsync("report-a@example.com");
        var (_, web) = await app.RegisterHostAsync(owner, "report-a-1", "web-1");
        await app.RegisterHostAsync(owner, "report-a-2", "db-1");
        await app.AddEmailChannelAsync(ownerId, "reports-a@example.com", channel => channel.DailyReport = true);
        await app.AddEmailChannelAsync(ownerId, "alerts-a@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80, configure: rule => rule.Severity = AlertSeverity.Warning);

        // db-1 goes quiet while web-1 runs hot with a failed service.
        app.Time.Advance(TimeSpan.FromMinutes(5));
        await web.SendSamplesAsync(MetricsIngestionTests.FullSample(Now.AddSeconds(-5)) with
        {
            Cpu = new CpuMetrics { UsagePercent = 97 },
            FailedServices = [new ServiceProblem { Name = "backup.service", State = "failed" }],
        });
        await app.EvaluateAsync();

        Assert.True(await app.QueueReportsAsync() >= 1);
        Assert.Equal(0, await app.QueueReportsAsync());
        await app.DispatchAsync();

        var report = Assert.Single(app.Email.SentTo("reports-a@example.com"), IsDailyReport);
        Assert.Contains("1 alert firing, 1 host offline, 1 failed service", report.TextBody);
        Assert.Contains("Hosts: 2, 1 offline", report.TextBody);
        Assert.Contains("- [Warning] CPU usage on web-1 above 80%", report.TextBody);
        Assert.Contains("- backup.service on web-1", report.TextBody);
        Assert.Contains("- db-1: offline", report.TextBody);
        Assert.Contains("<th", report.HtmlBody);
        Assert.DoesNotContain(app.Email.SentTo("alerts-a@example.com"), IsDailyReport);
    }

    [Fact]
    public async Task Reports_switched_on_start_with_the_next_period()
    {
        var (_, owner) = await OwnerAsync("report-b@example.com");
        var created = await owner.PostAsJsonAsync("/api/notification-channels",
            new { name = "Weekly", kind = "Webhook", target = "https://hooks.example.com/report-b", weeklyReport = true }, Ct);
        var channel = (await created.Content.ReadFromJsonAsync<NotificationChannelResponse>(TestJson.Options, Ct))!;
        Assert.True(channel.WeeklyReport);
        Assert.False(channel.DailyReport);

        await app.QueueReportsAsync();
        Assert.Empty(await app.DeliveriesAsync(channel.Id));

        app.Time.Advance(TimeSpan.FromDays(7));
        await app.QueueReportsAsync();
        Assert.Equal(NotificationKind.WeeklyReport, Assert.Single(await app.DeliveriesAsync(channel.Id)).Kind);

        await app.DispatchAsync();
        var posted = JsonNode.Parse(Assert.Single(app.Webhooks.BodiesSentTo("https://hooks.example.com/report-b")))!;
        Assert.Equal("report.weekly", (string?)posted["event"]);
        Assert.Equal(0, (int?)posted["report"]!["hostCount"]);
    }
}
