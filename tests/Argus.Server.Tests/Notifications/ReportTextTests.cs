using Argus.Server.Features.Alerts;
using Argus.Server.Features.Notifications;

namespace Argus.Server.Tests.Notifications;

public sealed class ReportTextTests
{
    private static readonly ReportOptions Options = new() { SendHourUtc = 7, WeeklyDay = DayOfWeek.Monday };

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static readonly ReportSummary Weekly = new(
        ReportKind.Weekly,
        From: At(9, 7, 7),
        To: At(9, 14, 7),
        HostCount: 3,
        OfflineHostCount: 1,
        new AlertTally(Critical: 1, Warning: 3, Info: 0),
        FiringCount: 2,
        Firing:
        [
            new ReportAlert("Disk usage on db-1 / above 90%", AlertSeverity.Critical, "db-1", At(9, 13, 22)),
            new ReportAlert("Memory <usage> on web-1 above 85%", AlertSeverity.Warning, "web-1", At(9, 14, 6)),
        ],
        Hosts: [new ReportHost("db-1", Online: false, 12.34, 88, 50, 91.2, AlertsRaised: 2)],
        FailedServices: []);

    [Fact]
    public void Reports_are_due_at_the_send_hour_and_on_the_weekly_day()
    {
        // 16 Sep 2026 is a Wednesday.
        Assert.Equal(At(9, 15, 7), ReportSchedule.LatestDue(ReportKind.Daily, At(9, 16, 6, 59), Options));
        Assert.Equal(At(9, 16, 7), ReportSchedule.LatestDue(ReportKind.Daily, At(9, 16, 7), Options));
        Assert.Equal(At(9, 14, 7), ReportSchedule.LatestDue(ReportKind.Weekly, At(9, 16, 12), Options));
        Assert.Equal(At(9, 7, 7), ReportSchedule.LatestDue(ReportKind.Weekly, At(9, 14, 6), Options));
        Assert.Equal(At(9, 16, 7), ReportSchedule.LatestDue(ReportKind.Weekly, At(9, 16, 8), new ReportOptions { WeeklyDay = DayOfWeek.Wednesday }));
    }

    [Fact]
    public void Report_phrases_read_naturally()
    {
        Assert.Equal("Weekly report, 7–14 Sep 2026", ReportText.Title(Weekly));
        Assert.Equal("Daily report, 14 Sep 2026", ReportText.Title(Weekly with { Kind = ReportKind.Daily, From = At(9, 13, 7) }));
        Assert.Equal("28 Aug – 4 Sep 2026", ReportText.Dates(Weekly with { From = At(8, 28, 7), To = At(9, 4, 7) }));
        Assert.Equal("7 Sep 2026, 07:00 to 14 Sep 2026, 07:00 UTC", ReportText.Period(Weekly));
        Assert.Equal("2 alerts firing, 1 host offline", ReportText.Headline(Weekly));
        Assert.Equal("All quiet", ReportText.Headline(Weekly with { FiringCount = 0, Firing = [], OfflineHostCount = 0 }));
        Assert.Equal("4 (1 critical, 3 warning)", ReportText.Raised(Weekly.AlertsRaised));
        Assert.Equal("None", ReportText.Raised(new AlertTally(0, 0, 0)));
    }

    [Fact]
    public void Report_emails_list_what_needs_attention()
    {
        var email = NotificationEmail.ForReport(Weekly, "Ops", "https://argus.example.com/");

        Assert.Equal("Weekly report, 7–14 Sep 2026", email.Subject);
        Assert.Contains("Alerts raised: 4 (1 critical, 3 warning)", email.Text);
        Assert.Contains("- [Critical] Disk usage on db-1 / above 90%, since 13 Sep, 22:00 UTC", email.Text);
        Assert.Contains("- db-1: offline, CPU 12.3% on average (88% at most), memory 50%, fullest disk 91.2%, 2 alerts", email.Text);
        Assert.Contains("Open Argus: https://argus.example.com", email.Text);
        Assert.Contains("Memory &lt;usage&gt; on web-1", email.Html);
        Assert.DoesNotContain("<usage>", email.Html);
    }

    [Fact]
    public void Chat_reports_carry_the_headline_and_what_is_firing()
    {
        var slack = WebhookPayloads.ForReport(NotificationChannelKind.Slack, Weekly, "https://argus.example.com");
        Assert.Equal("*<https://argus.example.com|Weekly report, 7–14 Sep 2026: 2 alerts firing, 1 host offline>*", (string?)slack["text"]);
        Assert.Contains("• [Warning] Memory &lt;usage&gt; on web-1 above 85%", (string?)slack["attachments"]![0]!["text"]);

        var discord = WebhookPayloads.ForReport(NotificationChannelKind.Discord, Weekly, publicUrl: null)["embeds"]![0]!;
        Assert.Equal(0xd0342a, (int?)discord["color"]);
        Assert.Contains("Firing now: 2", (string?)discord["description"]);
    }

    [Fact]
    public void Queued_reports_read_back_unchanged() =>
        Assert.Equal(Weekly.ToJson(), ReportSummary.FromJson(Weekly.ToJson()).ToJson());
}
