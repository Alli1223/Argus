using Argus.Server.Features.Alerts;
using Argus.Server.Features.Notifications;

namespace Argus.Server.Tests.Notifications;

public sealed class AlertEmailTests
{
    private static readonly Guid HostId = Guid.Parse("01a0aa6d-864f-7c6e-8840-23474b485dd1");

    private static readonly AlertNotification Fired = new(
        AlertEventKind.Fired,
        AlertId: Guid.Parse("01a0aa6d-8818-7184-8117-044c3a6aaf73"),
        HostId,
        HostName: "db <1>",
        Title: "Memory usage on db <1> above 90%",
        AlertSeverity.Warning,
        Reading: "93.2%, threshold 90%",
        RuleName: "Memory & swap",
        FiredAt: new DateTimeOffset(2026, 9, 16, 9, 5, 0, TimeSpan.Zero),
        ResolvedAt: null);

    [Fact]
    public void Fired_alerts_lead_with_their_severity()
    {
        var email = AlertEmail.Render(Fired, "Ops", "https://argus.example.com/");

        Assert.Equal("[Warning] Memory usage on db <1> above 90%", email.Subject);
        Assert.Contains("Reading: 93.2%, threshold 90%", email.Text);
        Assert.Contains("Started: 16 Sep 2026, 09:05 UTC", email.Text);
        Assert.Contains($"Open in Argus: https://argus.example.com/hosts/{HostId}", email.Text);
        Assert.Contains("Memory usage on db &lt;1&gt; above 90%", email.Html);
        Assert.Contains("Memory &amp; swap", email.Html);
        Assert.DoesNotContain("db <1>", email.Html);
    }

    [Fact]
    public void Resolved_alerts_say_how_long_they_lasted()
    {
        var email = AlertEmail.Render(
            Fired with { Kind = AlertEventKind.Resolved, ResolvedAt = Fired.FiredAt.AddMinutes(90) }, "Ops", publicUrl: null);

        Assert.Equal("Resolved: Memory usage on db <1> above 90%", email.Subject);
        Assert.Contains("Last reading: 93.2%, threshold 90%", email.Text);
        Assert.Contains("Resolved: 16 Sep 2026, 10:35 UTC, after 1 hour", email.Text);
        Assert.DoesNotContain("Open in Argus", email.Text);
        Assert.DoesNotContain("Open in Argus", email.Html);
    }

    [Fact]
    public void Readings_are_described_for_each_kind_of_alert()
    {
        Assert.Equal("No report for 6 minutes",
            AlertNotification.DescribeReading(new Alert { Metric = AlertMetric.HostOffline, Value = 360 }));
        Assert.Equal("Has never reported", AlertNotification.DescribeReading(new Alert { Metric = AlertMetric.HostOffline }));
        Assert.Equal("nginx.service has failed",
            AlertNotification.DescribeReading(new Alert { Metric = AlertMetric.ServiceFailed, ResourceKey = "nginx.service" }));
        Assert.Equal("60%, usually 10.2%", AlertNotification.DescribeReading(
            new Alert { Metric = AlertMetric.CpuUsage, Condition = AlertCondition.Anomaly, Value = 60, Baseline = 10.24 }));
        Assert.Equal("5 MB/s, threshold 1 MB/s", AlertNotification.DescribeReading(
            new Alert { Metric = AlertMetric.NetworkReceive, Value = 5 * 1024 * 1024, Threshold = 1024 * 1024 }));
    }

    [Fact]
    public void Queued_payloads_read_back_unchanged() =>
        Assert.Equal(Fired, AlertNotification.FromJson(Fired.ToJson()));
}
