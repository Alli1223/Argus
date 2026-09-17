using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;
using Microsoft.AspNetCore.Http;

namespace Argus.Server.Tests.Alerts;

public class ContainerDecisionTests
{
    [Theory]
    [InlineData("running", null, null, false, false, false)]
    [InlineData("running", "unhealthy", null, false, false, true)]
    [InlineData("running", "starting", null, false, false, false)]
    [InlineData("restarting", null, null, false, false, true)]
    [InlineData("dead", null, null, false, false, true)]
    [InlineData("exited", null, 1, false, false, true)]
    [InlineData("exited", null, 137, true, false, true)]
    [InlineData("exited", null, 0, false, false, false)]
    [InlineData("exited", null, 143, false, false, false)]
    [InlineData("exited", null, 0, false, true, true)]
    [InlineData("paused", null, null, false, false, false)]
    [InlineData("paused", null, null, false, true, true)]
    public void Containers_are_down_when_they_crash_fail_their_health_check_or_are_named_and_not_up(
        string state, string? health, int? exitCode, bool oomKilled, bool named, bool down)
    {
        Assert.Equal(down, AlertDecision.IsContainerDown(state, health, exitCode, oomKilled, named));
    }

    [Fact]
    public void Restarts_alert_above_the_threshold_and_clear_at_or_below_it()
    {
        Assert.Equal(new Verdict(true, false, 4), AlertDecision.EvaluateRestarts(4, 3));
        Assert.Equal(new Verdict(false, true, 3), AlertDecision.EvaluateRestarts(3, 3));
    }
}

public sealed class ContainerAlertTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => app.Time.GetUtcNow();

    private static ContainerInfo Container(string name, string state = "running", string? health = null, int? exitCode = null, int restarts = 0) => new()
    {
        Id = "c0ffee" + name,
        Name = name,
        Image = "nginx:1.29",
        State = state,
        Health = health,
        ExitCode = exitCode,
        RestartCount = restarts,
        CreatedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
    };

    private static MetricSample Report(DateTimeOffset time, params ContainerInfo[] containers) =>
        MetricsIngestionTests.FullSample(time) with { Containers = new ContainerReport { Items = containers } };

    private async Task<(Guid OwnerId, HttpClient Owner, Guid HostId, HttpClient Agent)> HostAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        var ownerId = (await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id;
        var (hostId, agent) = await app.RegisterHostAsync(owner, email + "-machine", "docker-1");
        return (ownerId, owner, hostId, agent);
    }

    [Fact]
    public async Task Crashed_and_unhealthy_containers_alert_after_the_duration_and_resolve_when_back_up()
    {
        var (ownerId, _, hostId, agent) = await HostAsync("container-alerts-a@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.ContainerDown, threshold: 0, durationSeconds: 120);

        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5), Container("web"), Container("api"), Container("job", "exited", exitCode: 0)));
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5),
            Container("web", "exited", exitCode: 1), Container("api", health: "unhealthy"), Container("job", "exited", exitCode: 0)));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(3));
        await app.EvaluateAsync();
        var alerts = await app.AlertsForHostAsync(hostId);
        Assert.Equal(["api", "web"], alerts.Select(alert => alert.ResourceKey).Order());
        Assert.Contains(alerts, alert => alert.Title == "web is down on docker-1");

        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5), Container("web"), Container("api", health: "unhealthy"), Container("job", "exited", exitCode: 0)));
        await app.EvaluateAsync();
        alerts = await app.AlertsForHostAsync(hostId);
        Assert.Equal(AlertStatus.Resolved, alerts.Single(alert => alert.ResourceKey == "web").Status);
        Assert.Equal(AlertStatus.Firing, alerts.Single(alert => alert.ResourceKey == "api").Status);
    }

    [Fact]
    public async Task A_rule_naming_a_container_alerts_when_it_is_stopped_on_purpose()
    {
        var (ownerId, _, hostId, agent) = await HostAsync("container-alerts-b@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.ContainerDown, threshold: 0, configure: rule => rule.ResourceFilter = "db");
        await app.AddRuleAsync(ownerId, AlertMetric.ContainerDown, threshold: 0);

        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5), Container("db", "exited", exitCode: 143), Container("cache", "exited", exitCode: 143)));
        await app.EvaluateAsync();

        Assert.Equal("db", Assert.Single(await app.AlertsForHostAsync(hostId)).ResourceKey);
    }

    [Fact]
    public async Task Restart_loops_alert_and_clear_once_restarts_stop()
    {
        var (ownerId, _, hostId, agent) = await HostAsync("container-alerts-c@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.ContainerRestarts, threshold: 3, durationSeconds: 600);

        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5), Container("worker", restarts: 1)));
        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5), Container("worker", "restarting", restarts: 3)));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Report(Now.AddSeconds(-5), Container("worker", restarts: 5)));
        await app.EvaluateAsync();
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(("worker keeps restarting on docker-1", 4d), (alert.Title, alert.Value!.Value));

        app.Time.Advance(TimeSpan.FromMinutes(11));
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Resolved, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);
    }

    [Fact]
    public async Task Container_rules_are_checked_when_saved()
    {
        var owner = await app.CreateOwnerAsync("container-alerts-d@example.com", Roles.User);

        async Task<IDictionary<string, string[]>> ErrorsAsync(object request)
        {
            var response = await owner.PostAsJsonAsync("/api/alert-rules", request, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors;
        }

        var loop = new { name = "Loop", metric = "ContainerRestarts", threshold = 2.5, durationSeconds = 30, severity = "Warning" };
        var errors = await ErrorsAsync(loop);
        Assert.Contains("threshold", errors.Keys);
        Assert.Contains("durationSeconds", errors.Keys);
        Assert.Contains("condition", (await ErrorsAsync(new { name = "x", metric = "ContainerRestarts", condition = "Anomaly", threshold = 3, durationSeconds = 600, severity = "Warning" })).Keys);

        var created = await owner.PostAsJsonAsync("/api/alert-rules",
            new { name = "Web down", metric = "ContainerDown", threshold = 0, durationSeconds = 0, severity = "Critical", resourceFilter = "web" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
