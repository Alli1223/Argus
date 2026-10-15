using System.Net;
using System.Net.Http.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Auth;
using Argus.Server.Features.Metrics;
using Argus.Server.Features.Power;
using Argus.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Argus.Server.Tests.Infrastructure;
using Argus.Server.Tests.Metrics;

namespace Argus.Server.Tests.Alerts;

public class UpsDecisionTests
{
    [Theory]
    [InlineData("OL", false, false)]
    [InlineData("OL CHRG", false, false)]
    [InlineData("OB DISCHRG", true, false)]
    [InlineData("OB DISCHRG LB", true, true)]
    [InlineData("ob lb", true, true)]
    [InlineData("", false, false)]
    public void Status_flags_say_whether_it_is_on_battery_and_nearly_empty(string status, bool onBattery, bool lowBattery)
    {
        Assert.Equal(onBattery, UpsStatus.OnBattery(status));
        Assert.Equal(lowBattery, UpsStatus.LowBattery(status));
    }

    [Theory]
    [InlineData(30, "30 s")]
    [InlineData(300, "5 min")]
    [InlineData(450, "7.5 min")]
    public void Runtime_thresholds_read_in_minutes(double seconds, string text) =>
        Assert.Equal(text, AlertDecision.FormatThreshold(AlertMetric.UpsBatteryRuntime, seconds));

    [Fact]
    public void Readings_on_battery_only_clear_once_line_power_is_back()
    {
        var firing = new Verdict(Fire: true, Clear: false, Value: 30);
        Assert.Equal(firing, AlertDecision.ClearOnLinePower(firing, null));
        Assert.Equal(firing, AlertDecision.ClearOnLinePower(firing, false));
        Assert.Equal(new Verdict(Fire: false, Clear: true, Value: 30), AlertDecision.ClearOnLinePower(firing, true));
    }
}

public sealed class UpsAlertTests(AlertsFixture app) : IClassFixture<AlertsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => app.Time.GetUtcNow();

    private static MetricSample Reading(DateTimeOffset time, string status, double charge, string name = "ups") =>
        MetricsIngestionTests.FullSample(time) with
        {
            Ups = [new UpsMetrics { Name = name, Model = "Back-UPS ES 700G", Status = status, BatteryChargePercent = charge, BatteryRuntimeSeconds = 900, LoadPercent = 20 }],
        };

    private async Task<(Guid OwnerId, HttpClient Owner, Guid HostId, HttpClient Agent)> HostAsync(string email)
    {
        var owner = await app.CreateOwnerAsync(email);
        var ownerId = (await owner.GetJsonAsync<CurrentUserResponse>("/api/auth/me"))!.Id;
        var (hostId, agent) = await app.RegisterHostAsync(owner, email + "-machine", "nas");
        return (ownerId, owner, hostId, agent);
    }

    [Fact]
    public async Task Running_on_battery_alerts_and_resolves_when_line_power_returns()
    {
        var (ownerId, _, hostId, agent) = await HostAsync("ups-alerts-a@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.UpsOnBattery, threshold: 0);

        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OL", 100));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OB DISCHRG", 97), Reading(Now.AddSeconds(-5), "OB DISCHRG", 95));
        await app.EvaluateAsync();
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal(("ups", "UPS ups on nas is on battery", AlertStatus.Firing), (alert.ResourceKey, alert.Title, alert.Status));

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OL CHRG", 95), Reading(Now.AddSeconds(-5), "OL CHRG", 95));
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Resolved, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);
    }

    [Fact]
    public async Task Low_charge_alerts_per_ups_once_it_lasts_the_duration()
    {
        var (ownerId, _, hostId, agent) = await HostAsync("ups-alerts-b@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.UpsBatteryCharge, threshold: 40, durationSeconds: 60,
            configure: rule => rule.Operator = AlertOperator.Below);

        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-5), "OL CHRG", 16), Reading(Now.AddSeconds(-5), "OL", 100, name: "spare"));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromSeconds(50));
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-5), "OL CHRG", 17), Reading(Now.AddSeconds(-5), "OL", 100, name: "spare"));
        await app.EvaluateAsync();
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal("ups", alert.ResourceKey);
        Assert.Equal("UPS battery charge of ups on nas below 40%", alert.Title);
    }

    private Task<List<PowerAction>> PowerActionsAsync(Guid hostId) =>
        app.WithScopeAsync(services => services.GetRequiredService<ArgusDbContext>().PowerActions
            .AsNoTracking()
            .Where(action => action.HostId == hostId)
            .OrderBy(action => action.At)
            .ToListAsync(Ct));

    /// <summary>Rules' power actions are carried out in the background, a moment after the alert.</summary>
    private async Task<List<PowerAction>> WaitForPowerActionsAsync(Guid hostId, int count)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await PowerActionsAsync(hostId) is { } actions && actions.Count >= count)
            {
                return actions;
            }

            await Task.Delay(50, Ct);
        }

        return await PowerActionsAsync(hostId);
    }

    [Fact]
    public async Task Rules_shut_hosts_down_when_the_ups_runs_low_on_battery_and_wake_them_after()
    {
        var (ownerId, owner, hostId, agent) = await HostAsync("ups-alerts-d@example.com");
        var (desktopId, _) = await app.RegisterHostAsync(owner, "ups-alerts-d-desktop", "desktop");
        var rule = await app.AddRuleAsync(ownerId, AlertMetric.UpsChargeOnBattery, threshold: 40, configure: rule =>
        {
            rule.Operator = AlertOperator.Below;
            rule.ShutdownHostIds = [desktopId];
            rule.WakeOnResolve = true;
        });

        // A battery low while it charges on line power is no reason to shut anything down.
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OL CHRG", 16), Reading(Now.AddSeconds(-5), "OL CHRG", 16));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OB DISCHRG", 45), Reading(Now.AddSeconds(-5), "OB DISCHRG", 38));
        await app.EvaluateAsync();
        Assert.Empty(await app.AlertsForHostAsync(hostId));

        app.Time.Advance(TimeSpan.FromSeconds(30));
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OB DISCHRG", 36), Reading(Now.AddSeconds(-5), "OB DISCHRG", 34));
        await app.EvaluateAsync();
        var alert = Assert.Single(await app.AlertsForHostAsync(hostId));
        Assert.Equal("UPS ups on nas is on battery with charge below 40%", alert.Title);

        // The desktop's agent does not allow power actions, so the attempt is recorded as refused.
        var shutdown = Assert.Single(await WaitForPowerActionsAsync(desktopId, 1));
        Assert.Equal((PowerActionKind.Shutdown, false, rule.Id, alert.Title), (shutdown.Kind, shutdown.Succeeded, shutdown.RuleId, shutdown.Reason));
        Assert.Null(shutdown.RequestedBy);

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-20), "OL CHRG", 35), Reading(Now.AddSeconds(-5), "OL CHRG", 36));
        await app.EvaluateAsync();
        Assert.Equal(AlertStatus.Resolved, Assert.Single(await app.AlertsForHostAsync(hostId)).Status);

        var actions = await WaitForPowerActionsAsync(desktopId, 2);
        Assert.Equal([PowerActionKind.Shutdown, PowerActionKind.Wake], actions.Select(action => action.Kind));
        Assert.Equal("Resolved: " + alert.Title, actions[1].Reason);
    }

    [Fact]
    public async Task Rule_actions_only_name_the_owners_own_hosts()
    {
        var (_, owner, hostId, _) = await HostAsync("ups-alerts-e@example.com");
        var (_, _, strangerHostId, _) = await HostAsync("ups-alerts-e-stranger@example.com");

        var refused = await owner.PostAsJsonAsync("/api/alert-rules",
            new { name = "Low", metric = "UpsChargeOnBattery", @operator = "Below", threshold = 40, shutdownHostIds = new[] { strangerHostId } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var noHosts = await owner.PostAsJsonAsync("/api/alert-rules",
            new { name = "Low", metric = "UpsChargeOnBattery", @operator = "Below", threshold = 40, wakeOnResolve = true }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, noHosts.StatusCode);

        var created = await owner.PostAsJsonAsync("/api/alert-rules",
            new { name = "Low", metric = "UpsChargeOnBattery", @operator = "Below", threshold = 40, shutdownHostIds = new[] { hostId, hostId }, wakeOnResolve = true }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var rule = (await created.Content.ReadFromJsonAsync<AlertRuleResponse>(TestJson.Options, Ct))!;
        Assert.Equal([hostId], rule.ShutdownHostIds);
        Assert.True(rule.WakeOnResolve);
    }

    [Fact]
    public async Task Rules_can_name_one_ups()
    {
        var (ownerId, owner, hostId, agent) = await HostAsync("ups-alerts-c@example.com");
        await app.AddRuleAsync(ownerId, AlertMetric.UpsOnBattery, threshold: 0, configure: rule => rule.ResourceFilter = "spare");

        await agent.SendSamplesAsync(Reading(Now.AddSeconds(-5), "OB", 90), Reading(Now.AddSeconds(-5), "OB", 90, name: "spare"));
        await app.EvaluateAsync();

        Assert.Equal("spare", Assert.Single(await app.AlertsForHostAsync(hostId)).ResourceKey);

        var created = await owner.PostAsJsonAsync("/api/alert-rules",
            new { name = "Runtime", metric = "UpsBatteryRuntime", @operator = "Below", threshold = 300, durationSeconds = 60, resourceFilter = "ups" }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
}
