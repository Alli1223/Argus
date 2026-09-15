using Argus.Server.Features.Alerts;

namespace Argus.Server.Tests.Alerts;

public class AlertDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private static WindowStats Stats(
        double min, double max, int firstSecondsAgo = 300, int lastSecondsAgo = 5, double? recentMin = null, double? recentMax = null) =>
        new(
            Samples: 20,
            FirstTime: Now.AddSeconds(-firstSecondsAgo),
            LastTime: Now.AddSeconds(-lastSecondsAgo),
            Min: min,
            Max: max,
            Average: (min + max) / 2,
            RecentSamples: 8,
            RecentMin: recentMin ?? min,
            RecentMax: recentMax ?? max);

    [Fact]
    public void Fires_when_every_sample_in_a_covered_fresh_window_breaches()
    {
        var verdict = AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, Stats(91, 99), Now, Tolerance);

        Assert.True(verdict.Fire);
        Assert.False(verdict.Clear);
        Assert.Equal(95, verdict.Value);
    }

    [Fact]
    public void One_good_sample_in_the_window_prevents_firing()
    {
        var verdict = AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, Stats(40, 99), Now, Tolerance);

        Assert.False(verdict.Fire);
        Assert.False(verdict.Clear);
    }

    [Fact]
    public void Data_that_does_not_reach_back_over_the_duration_does_not_fire()
    {
        var verdict = AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, Stats(95, 99, firstSecondsAgo: 120), Now, Tolerance);

        Assert.False(verdict.Fire);
    }

    [Fact]
    public void Stale_data_does_not_fire()
    {
        var verdict = AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, Stats(95, 99, lastSecondsAgo: 120), Now, Tolerance);

        Assert.False(verdict.Fire);
    }

    [Fact]
    public void Clears_only_when_every_recent_sample_is_back_within_the_threshold()
    {
        Assert.True(AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, Stats(10, 99, recentMin: 10, recentMax: 60), Now, Tolerance).Clear);
        Assert.False(AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, Stats(10, 99, recentMin: 10, recentMax: 95), Now, Tolerance).Clear);
    }

    [Fact]
    public void Below_rules_mirror_above_rules()
    {
        Assert.True(AlertDecision.Evaluate(AlertOperator.Below, 10, FiveMinutes, Stats(1, 9), Now, Tolerance).Fire);
        Assert.False(AlertDecision.Evaluate(AlertOperator.Below, 10, FiveMinutes, Stats(1, 30), Now, Tolerance).Fire);
        Assert.True(AlertDecision.Evaluate(AlertOperator.Below, 10, FiveMinutes, Stats(1, 30, recentMin: 20, recentMax: 30), Now, Tolerance).Clear);
    }

    [Fact]
    public void No_data_keeps_the_current_state()
    {
        var verdict = AlertDecision.Evaluate(AlertOperator.Above, 90, FiveMinutes, default, Now, Tolerance);

        Assert.False(verdict.Fire);
        Assert.False(verdict.Clear);
        Assert.Null(verdict.Value);
    }

    [Fact]
    public void Hosts_are_offline_once_silent_for_the_duration()
    {
        Assert.True(AlertDecision.EvaluateOffline(Now.AddMinutes(-6), FiveMinutes, Now).Fire);
        Assert.True(AlertDecision.EvaluateOffline(null, FiveMinutes, Now).Fire);
        var online = AlertDecision.EvaluateOffline(Now.AddSeconds(-20), FiveMinutes, Now);
        Assert.False(online.Fire);
        Assert.True(online.Clear);
        Assert.Equal(20, online.Value);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(60, 60)]
    [InlineData(600, 120)]
    public void Resolve_windows_stay_between_the_tolerance_and_two_minutes(int durationSeconds, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), AlertDecision.ResolveWindow(TimeSpan.FromSeconds(durationSeconds), Tolerance));
    }

    [Fact]
    public void Titles_describe_what_happened_where()
    {
        var cpu = new AlertRule { Metric = AlertMetric.CpuUsage, Operator = AlertOperator.Above, Threshold = 90 };
        var disk = new AlertRule { Metric = AlertMetric.DiskUsage, Operator = AlertOperator.Above, Threshold = 85.5 };
        var network = new AlertRule { Metric = AlertMetric.NetworkReceive, Operator = AlertOperator.Above, Threshold = 10 * 1024 * 1024 };
        var offline = new AlertRule { Metric = AlertMetric.HostOffline };

        Assert.Equal("CPU usage on web-1 above 90%", AlertDecision.Title(cpu, "web-1", ""));
        Assert.Equal("Disk usage on web-1 /var above 85.5%", AlertDecision.Title(disk, "web-1", "/var"));
        Assert.Equal("Network receive on web-1 above 10 MB/s", AlertDecision.Title(network, "web-1", ""));
        Assert.Equal("web-1 is offline", AlertDecision.Title(offline, "web-1", ""));
    }
}
