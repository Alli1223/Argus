using Argus.Server.Features.Alerts;

namespace Argus.Server.Tests.Alerts;

public sealed class AnomalyDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

    // A week of 5-minute averages around 20% with a spread of 4 points.
    private static readonly BaselineStats Usual = new(Buckets: 2000, Mean: 20, StdDev: 4);

    /// <summary>Ten minutes of samples averaging <paramref name="average"/>, the newest just in.</summary>
    private static WindowStats Window(double average) => new(
        Samples: 40,
        FirstTime: Now - Duration,
        LastTime: Now.AddSeconds(-5),
        Min: average - 1,
        Max: average + 1,
        Average: average,
        RecentSamples: 8,
        RecentMin: average - 1,
        RecentMax: average + 1);

    private static Verdict Evaluate(
        double average, AlertOperator op = AlertOperator.Above, BaselineStats? baseline = null, WindowStats? window = null) =>
        AlertDecision.EvaluateAnomaly(
            op, sensitivity: 3, AlertMetric.CpuUsage, Duration, window ?? Window(average), baseline ?? Usual, Now, Tolerance);

    [Fact]
    public void Fires_when_the_average_strays_beyond_the_band()
    {
        // The band reaches 3 × 4 = 12 points above the usual 20%.
        Assert.False(Evaluate(31).Fire);

        var verdict = Evaluate(33);
        Assert.True(verdict.Fire);
        Assert.Equal(33, verdict.Value);
        Assert.Equal(20, verdict.Baseline);
    }

    [Fact]
    public void Looks_only_in_the_rules_direction()
    {
        Assert.False(Evaluate(5).Fire);
        Assert.True(Evaluate(5, AlertOperator.Below).Fire);
        Assert.False(Evaluate(33, AlertOperator.Below).Fire);
    }

    [Fact]
    public void A_steady_metric_still_needs_a_real_change()
    {
        // Three deviations of a steady 2% is under a point; the 5-point minimum decides instead.
        var steady = new BaselineStats(Buckets: 2000, Mean: 2, StdDev: 0.1);
        Assert.False(Evaluate(6, baseline: steady).Fire);
        Assert.True(Evaluate(7.5, baseline: steady).Fire);
    }

    [Fact]
    public void Stays_quiet_until_there_is_a_day_of_history()
    {
        Assert.False(Evaluate(90, baseline: Usual with { Buckets = 100 }).Fire);
        Assert.False(Evaluate(90, baseline: default(BaselineStats)).Fire);
    }

    [Fact]
    public void Clears_only_once_the_average_is_well_inside_the_band()
    {
        // Under the band's edge at 32%, but not by much: the alert holds.
        var edge = Evaluate(30);
        Assert.False(edge.Fire);
        Assert.False(edge.Clear);

        // Within 80% of the band, 29.6%.
        Assert.True(Evaluate(29).Clear);
    }

    [Fact]
    public void Needs_fresh_samples_over_the_whole_window()
    {
        var stale = Window(40) with { LastTime = Now.AddMinutes(-5) };
        Assert.False(Evaluate(40, window: stale).Fire);
        Assert.False(Evaluate(10, window: Window(10) with { LastTime = Now.AddMinutes(-5) }).Clear);

        var partial = Window(40) with { FirstTime = Now.AddMinutes(-2) };
        Assert.False(Evaluate(40, window: partial).Fire);
    }

    [Fact]
    public void Titles_say_which_way_the_reading_strayed()
    {
        var rule = new AlertRule { Metric = AlertMetric.MemoryUsage, Condition = AlertCondition.Anomaly, Operator = AlertOperator.Below };

        Assert.Equal("Memory usage on db-1 unusually low", AlertDecision.Title(rule, "db-1", ""));
    }
}
