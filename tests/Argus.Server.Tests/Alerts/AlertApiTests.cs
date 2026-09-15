using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Dashboard;
using Argus.Server.Features.Users;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http;

namespace Argus.Server.Tests.Alerts;

public sealed class AlertApiTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object CpuRule(string name = "Hot CPU", double threshold = 85) =>
        new { name, metric = "CpuUsage", threshold, durationSeconds = 0, severity = "Critical" };

    private async Task<(Guid HostId, HttpClient Agent, HttpClient Owner)> HostAsync(string email, string hostname = "web-1")
    {
        var owner = await app.CreateOwnerAsync(email);
        var (hostId, agent) = await app.RegisterHostAsync(owner, email + "-machine", hostname);
        return (hostId, agent, owner);
    }

    private async Task FireCpuAlertAsync(HttpClient owner, HttpClient agent)
    {
        (await owner.PostAsJsonAsync("/api/alert-rules", CpuRule(), Ct)).EnsureSuccessStatusCode();
        await agent.SendSamplesAsync(
            MetricsIngestionTests.FullSample(app.Time.GetUtcNow().AddSeconds(-5)) with { Cpu = new CpuMetrics { UsagePercent = 99 } });
        await app.EvaluateAsync();
    }

    [Fact]
    public async Task Rules_are_created_updated_and_deleted()
    {
        var (hostId, _, owner) = await HostAsync("rules-a@example.com");

        var created = await owner.PostAsJsonAsync("/api/alert-rules",
            new { name = "Busy DB", metric = "CpuUsage", threshold = 75, durationSeconds = 600, severity = "Warning", hostId }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = (await created.Content.ReadFromJsonAsync<AlertRuleResponse>(TestJson.Options, Ct))!;
        Assert.Equal(AlertMetric.CpuUsage, rule.Metric);
        Assert.Equal("web-1", rule.HostName);
        Assert.True(rule.Enabled);

        var updated = await owner.PutAsJsonAsync($"/api/alert-rules/{rule.Id}",
            new { name = "Busy DB", metric = "CpuUsage", threshold = 95, durationSeconds = 60, severity = "Critical", enabled = false }, Ct);
        updated.EnsureSuccessStatusCode();
        var changed = (await updated.Content.ReadFromJsonAsync<AlertRuleResponse>(TestJson.Options, Ct))!;
        Assert.Equal(95, changed.Threshold);
        Assert.Equal(AlertSeverity.Critical, changed.Severity);
        Assert.False(changed.Enabled);
        Assert.Null(changed.HostId);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/alert-rules/{rule.Id}", Ct)).StatusCode);
        Assert.Empty((await owner.GetJsonAsync<List<AlertRuleResponse>>("/api/alert-rules"))!);
    }

    [Fact]
    public async Task Rule_requests_are_checked_against_the_metric()
    {
        var (hostId, _, owner) = await HostAsync("rules-b@example.com");
        var (strangerHostId, _, _) = await HostAsync("rules-c@example.com");

        async Task<IDictionary<string, string[]>> ErrorsFor(object request)
        {
            var response = await owner.PostAsJsonAsync("/api/alert-rules", request, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors;
        }

        Assert.Contains("threshold", (await ErrorsFor(new { name = "x", metric = "MemoryUsage", threshold = 150 })).Keys);
        Assert.Contains("durationSeconds", (await ErrorsFor(new { name = "x", metric = "HostOffline", durationSeconds = 10 })).Keys);
        Assert.Contains("resourceFilter", (await ErrorsFor(new { name = "x", metric = "CpuUsage", threshold = 90, resourceFilter = "/" })).Keys);
        Assert.Contains("tag", (await ErrorsFor(new { name = "x", metric = "CpuUsage", threshold = 90, hostId, tag = "prod" })).Keys);
        Assert.Contains("hostId", (await ErrorsFor(new { name = "x", metric = "CpuUsage", threshold = 90, hostId = strangerHostId })).Keys);
        Assert.Contains("metric", (await ErrorsFor(new { name = "x", threshold = 90 })).Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task New_accounts_start_with_the_default_rules()
    {
        var admin = await app.CreateOwnerAsync("rules-admin@example.com", Roles.Admin);
        var created = await admin.PostAsJsonAsync("/api/users",
            new { email = "rules-d@example.com", displayName = "Dee", password = AgentTestHelpers.Password, role = Roles.User }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var user = await app.CreateSignedInClientAsync("rules-d@example.com", AgentTestHelpers.Password);
        var rules = (await user.GetJsonAsync<List<AlertRuleResponse>>("/api/alert-rules"))!;

        Assert.Equal(
            [AlertMetric.CpuUsage, AlertMetric.MemoryUsage, AlertMetric.DiskUsage, AlertMetric.HostOffline],
            rules.Select(rule => rule.Metric).Order());
        Assert.All(rules, rule => Assert.True(rule.Enabled));
    }

    [Fact]
    public async Task Alerts_are_listed_filtered_and_acknowledged()
    {
        var (hostId, agent, owner) = await HostAsync("rules-e@example.com", "db-9");
        await FireCpuAlertAsync(owner, agent);

        var firing = await owner.GetJsonAsync<AlertPage>("/api/alerts?status=Firing");
        var alert = Assert.Single(firing!.Items);
        Assert.Equal(1, firing.Total);
        Assert.Equal("db-9", alert.HostName);
        Assert.Equal("Hot CPU", alert.RuleName);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Null(alert.AcknowledgedAt);
        Assert.Empty((await owner.GetJsonAsync<AlertPage>("/api/alerts?status=Resolved"))!.Items);
        Assert.Single((await owner.GetJsonAsync<AlertPage>($"/api/alerts?hostId={hostId}&severity=Critical"))!.Items);

        var acknowledged = await owner.PostAsync($"/api/alerts/{alert.Id}/acknowledge", null, Ct);
        acknowledged.EnsureSuccessStatusCode();
        var result = (await acknowledged.Content.ReadFromJsonAsync<AlertResponse>(TestJson.Options, Ct))!;
        Assert.NotNull(result.AcknowledgedAt);
        Assert.Equal("rules-e", result.AcknowledgedBy);

        var counts = await owner.GetJsonAsync<AlertCounts>("/api/alerts/summary");
        Assert.Equal(1, counts!.Critical);
        var dashboard = await owner.GetJsonAsync<DashboardSummary>("/api/dashboard/summary");
        Assert.Equal(1, dashboard!.ActiveAlerts.Critical);
    }

    [Fact]
    public async Task Other_users_cannot_see_or_acknowledge_alerts()
    {
        var (_, agent, owner) = await HostAsync("rules-f@example.com");
        await FireCpuAlertAsync(owner, agent);
        var alert = Assert.Single((await owner.GetJsonAsync<AlertPage>("/api/alerts"))!.Items);
        var stranger = await app.CreateOwnerAsync("rules-g@example.com");

        Assert.Empty((await stranger.GetJsonAsync<AlertPage>("/api/alerts"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/alerts/{alert.Id}/acknowledge", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_rule_resolves_its_alerts_but_keeps_the_history()
    {
        var (hostId, agent, owner) = await HostAsync("rules-h@example.com");
        await FireCpuAlertAsync(owner, agent);
        var rule = Assert.Single((await owner.GetJsonAsync<List<AlertRuleResponse>>("/api/alert-rules"))!);
        Assert.Equal(1, rule.FiringAlerts);

        (await owner.DeleteAsync($"/api/alert-rules/{rule.Id}", Ct)).EnsureSuccessStatusCode();

        var alert = Assert.Single((await owner.GetJsonAsync<AlertPage>($"/api/alerts?hostId={hostId}"))!.Items);
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.Null(alert.RuleId);
        Assert.Equal("CPU usage on web-1 above 85%", alert.Title);
    }
}
