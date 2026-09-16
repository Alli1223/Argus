using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Notifications;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;

namespace Argus.Server.Tests.Notifications;

public sealed class NotificationTests(NotificationsFixture app) : IClassFixture<NotificationsFixture>
{
    private DateTimeOffset Now => app.Time.GetUtcNow();

    private static MetricSample Cpu(DateTimeOffset time, double percent) =>
        MetricsIngestionTests.FullSample(time) with { Cpu = new CpuMetrics { UsagePercent = percent } };

    private async Task<(Guid OwnerId, HttpClient Owner)> OwnerAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        return ((await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id, owner);
    }

    /// <summary>Fires a CPU alert on the host, then a minute later lets it resolve.</summary>
    private async Task FireAndResolveAsync(HttpClient agent)
    {
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-5), 97));
        await app.EvaluateAsync();
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-20), 10), Cpu(Now.AddSeconds(-5), 12));
        await app.EvaluateAsync();
    }

    [Fact]
    public async Task Alerts_are_emailed_when_they_fire_and_when_they_resolve()
    {
        var (ownerId, owner) = await OwnerAsync("notify-a@example.com");
        var (hostId, agent) = await app.RegisterHostAsync(owner, "notify-a-1", "web-1");
        await app.AddEmailChannelAsync(ownerId, "ops-a@example.com, oncall-a@example.com", channel => channel.Name = "Ops");
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80, configure: rule => rule.Name = "Hot CPU");

        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-5), 97));
        await app.EvaluateAsync();
        Assert.Equal(new DispatchResult(Sent: 1, Retrying: 0, Failed: 0), await app.DispatchAsync());

        var fired = Assert.Single(app.Email.SentTo("ops-a@example.com"));
        Assert.Equal(["ops-a@example.com", "oncall-a@example.com"], fired.To.Mailboxes.Select(mailbox => mailbox.Address));
        Assert.Equal("[Critical] CPU usage on web-1 above 80%", fired.Subject);
        Assert.Contains("Reading: 97%, threshold 80%", fired.TextBody);
        Assert.Contains("Rule: Hot CPU", fired.TextBody);
        Assert.Contains("channel \"Ops\"", fired.TextBody);
        Assert.Contains($"{NotificationsFixture.PublicUrl}/hosts/{hostId}", fired.HtmlBody);

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-20), 10), Cpu(Now.AddSeconds(-5), 12));
        await app.EvaluateAsync();
        await app.DispatchAsync();

        var resolved = app.Email.SentTo("ops-a@example.com")[1];
        Assert.Equal("Resolved: CPU usage on web-1 above 80%", resolved.Subject);
        Assert.Contains("after 1 minute", resolved.TextBody);
    }

    [Fact]
    public async Task Channels_pass_on_only_the_alerts_they_ask_for()
    {
        var (ownerId, owner) = await OwnerAsync("notify-b@example.com");
        var (_, agent) = await app.RegisterHostAsync(owner, "notify-b-1");
        var (strangerId, _) = await OwnerAsync("notify-c@example.com");
        await app.AddEmailChannelAsync(ownerId, "everything-b@example.com", channel => channel.MinimumSeverity = AlertSeverity.Info);
        await app.AddEmailChannelAsync(ownerId, "critical-b@example.com", channel => channel.MinimumSeverity = AlertSeverity.Critical);
        await app.AddEmailChannelAsync(ownerId, "firing-b@example.com", channel => channel.NotifyOnResolved = false);
        await app.AddEmailChannelAsync(ownerId, "off-b@example.com", channel => channel.Enabled = false);
        await app.AddEmailChannelAsync(strangerId, "stranger-b@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80, configure: rule => rule.Severity = AlertSeverity.Warning);

        await FireAndResolveAsync(agent);
        await app.DispatchAsync();

        Assert.Equal(2, app.Email.SentTo("everything-b@example.com").Count);
        Assert.Single(app.Email.SentTo("firing-b@example.com"));
        Assert.Empty(app.Email.SentTo("critical-b@example.com"));
        Assert.Empty(app.Email.SentTo("off-b@example.com"));
        Assert.Empty(app.Email.SentTo("stranger-b@example.com"));
    }

    [Fact]
    public async Task Failed_sends_are_retried_with_growing_gaps_then_given_up()
    {
        var (ownerId, owner) = await OwnerAsync("notify-d@example.com");
        var (_, agent) = await app.RegisterHostAsync(owner, "notify-d-1");
        var channel = await app.AddEmailChannelAsync(ownerId, "retry-d@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.CpuUsage, threshold: 80);
        await agent.SendSamplesAsync(Cpu(Now.AddSeconds(-5), 97));
        await app.EvaluateAsync();

        app.Email.FailWith = "Connection refused";
        try
        {
            Assert.Equal(new DispatchResult(Sent: 0, Retrying: 1, Failed: 0), await app.DispatchAsync());
            var delivery = Assert.Single(await app.DeliveriesAsync(channel.Id));
            Assert.Equal((DeliveryStatus.Pending, 1, "Connection refused"), (delivery.Status, delivery.Attempts, delivery.LastError));
            Assert.Equal(Now.AddMinutes(1), delivery.NextAttemptAt);

            // Not due again yet.
            Assert.Equal(0, (await app.DispatchAsync()).Total);

            app.Time.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(new DispatchResult(Sent: 0, Retrying: 1, Failed: 0), await app.DispatchAsync());
            Assert.Equal(Now.AddMinutes(5), Assert.Single(await app.DeliveriesAsync(channel.Id)).NextAttemptAt);

            // The fixture allows three attempts.
            app.Time.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(new DispatchResult(Sent: 0, Retrying: 0, Failed: 1), await app.DispatchAsync());
            Assert.Equal(DeliveryStatus.Failed, Assert.Single(await app.DeliveriesAsync(channel.Id)).Status);
        }
        finally
        {
            app.Email.FailWith = null;
        }

        app.Time.Advance(TimeSpan.FromHours(4));
        Assert.Equal(0, (await app.DispatchAsync()).Total);
        Assert.Empty(app.Email.SentTo("retry-d@example.com"));
    }
}
