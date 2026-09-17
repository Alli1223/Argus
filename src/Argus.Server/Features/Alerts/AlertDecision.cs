using System.Globalization;

namespace Argus.Server.Features.Alerts;

/// <summary>Aggregates of one metric for one host (and resource) over a rule's window.</summary>
public readonly record struct WindowStats(
    int Samples,
    DateTimeOffset? FirstTime,
    DateTimeOffset? LastTime,
    double? Min,
    double? Max,
    double? Average,
    int RecentSamples,
    double? RecentMin,
    double? RecentMax);

/// <summary>A host's usual level of one metric: the mean and spread of its 5-minute averages.</summary>
public readonly record struct BaselineStats(int Buckets, double Mean, double StdDev);

/// <summary>
/// Whether a rule's condition is met (fire), clearly over (clear), or neither. Anomaly verdicts also
/// carry the usual level the value was compared with.
/// </summary>
public readonly record struct Verdict(bool Fire, bool Clear, double? Value, double? Baseline = null);

/// <summary>When alerts fire and resolve. Kept free of I/O so the rules are easy to test.</summary>
public static class AlertDecision
{
    /// <summary>The longest run of good samples needed before an alert resolves.</summary>
    public static readonly TimeSpan MaxResolveWindow = TimeSpan.FromMinutes(2);

    /// <summary>How far back to look: the rule's duration, but never less than the sampling tolerance.</summary>
    public static TimeSpan Window(TimeSpan duration, TimeSpan tolerance) => duration > tolerance ? duration : tolerance;

    /// <summary>How long values must be back to normal before an alert resolves.</summary>
    public static TimeSpan ResolveWindow(TimeSpan duration, TimeSpan tolerance)
    {
        var upper = MaxResolveWindow > tolerance ? MaxResolveWindow : tolerance;
        return duration < tolerance ? tolerance : duration > upper ? upper : duration;
    }

    /// <summary>
    /// Fires when every sample in the window breaches the threshold, the samples reach back over the
    /// whole duration and the newest one is fresh. Clears once every sample in the resolve window is
    /// back within the threshold. Anything in between keeps the current state, which prevents flapping.
    /// Without data the state is kept too: an offline host is the host-offline rule's business.
    /// </summary>
    public static Verdict Evaluate(
        AlertOperator op, double threshold, TimeSpan duration, WindowStats stats, DateTimeOffset now, TimeSpan tolerance)
    {
        if (stats.Samples == 0 || stats.FirstTime is null || stats.LastTime is null)
        {
            return new Verdict(Fire: false, Clear: false, Value: null);
        }

        var fresh = stats.LastTime >= now - tolerance;
        var covered = stats.FirstTime <= now - duration + tolerance;
        var breaching = op == AlertOperator.Above ? stats.Min > threshold : stats.Max < threshold;
        var cleared = stats.RecentSamples > 0
            && (op == AlertOperator.Above ? stats.RecentMax <= threshold : stats.RecentMin >= threshold);

        return new Verdict(Fire: fresh && covered && breaching, Clear: cleared, Value: stats.Average);
    }

    /// <summary>A host is offline once it has been silent for the rule's duration.</summary>
    public static Verdict EvaluateOffline(DateTimeOffset? lastSeen, TimeSpan duration, DateTimeOffset now)
    {
        var silence = lastSeen is { } seen ? now - seen : (TimeSpan?)null;
        var offline = silence is null || silence >= duration;
        return new Verdict(Fire: offline, Clear: !offline, Value: silence?.TotalSeconds);
    }

    /// <summary>
    /// A failed service alerts once it has been failing for the rule's duration; with no duration, at
    /// once (even if the agent's clock runs a little ahead). It clears when the service stops being
    /// reported as failed, which the evaluator handles.
    /// </summary>
    public static Verdict EvaluateServiceFailure(DateTimeOffset since, TimeSpan duration, DateTimeOffset now) =>
        new(Fire: duration <= TimeSpan.Zero || now - since >= duration, Clear: false, Value: null);

    /// <summary>
    /// Exit codes of a container that was stopped rather than crashed: clean exits, and the signals
    /// <c>docker stop</c> and Ctrl+C send (SIGTERM, then SIGKILL after the timeout; SIGINT).
    /// </summary>
    private static readonly HashSet<int> StopCodes = [0, 130, 137, 143];

    /// <summary>
    /// Whether a container is down: unhealthy, restarting, dead or crashed. A rule that names the container
    /// wants it up, so for it a container stopped on purpose, paused or never started is down too; a rule
    /// over every container leaves those alone, as that is how one-off jobs end and how people stop things.
    /// </summary>
    public static bool IsContainerDown(string state, string? health, int? exitCode, bool oomKilled, bool named) => state switch
    {
        "running" => health == "unhealthy",
        "restarting" or "dead" => true,
        "exited" => named || oomKilled || exitCode is not { } code || !StopCodes.Contains(code),
        "paused" or "created" => named,
        _ => false,
    };

    /// <summary>A container restarting more often than the threshold in the rule's window; it clears once it no longer does.</summary>
    public static Verdict EvaluateRestarts(int restarts, double threshold) =>
        new(Fire: restarts > threshold, Clear: restarts <= threshold, Value: restarts);

    /// <summary>How much history anomaly rules learn a host's usual level from.</summary>
    public static readonly TimeSpan BaselinePeriod = TimeSpan.FromDays(7);

    /// <summary>Anomaly rules stay quiet until a host has a day of 5-minute averages.</summary>
    public const int MinimumBaselineBuckets = 288;

    /// <summary>The shortest window anomaly rules average over, so one noisy reading cannot trip them.</summary>
    public static readonly TimeSpan MinimumAnomalyDuration = TimeSpan.FromMinutes(5);

    /// <summary>An anomaly alert resolves once the value is back within this share of the band.</summary>
    private const double AnomalyClearFraction = 0.8;

    /// <summary>
    /// Fires when the average over the window strays from the usual level, in the rule's direction, by
    /// more than <paramref name="sensitivity"/> standard deviations of the host's 5-minute averages
    /// (and by at least the metric's minimum deviation, so a flat line does not alert on noise). As for
    /// thresholds, the samples must reach back over the whole duration and the newest must be fresh.
    /// It clears once the average is well back inside the band, so a value at the edge cannot flap.
    /// Without a day of history nothing fires.
    /// </summary>
    public static Verdict EvaluateAnomaly(
        AlertOperator op,
        double sensitivity,
        AlertMetric metric,
        TimeSpan duration,
        WindowStats stats,
        BaselineStats baseline,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (stats.Samples == 0 || stats.FirstTime is null || stats.LastTime is null || stats.Average is not { } average
            || baseline.Buckets < MinimumBaselineBuckets)
        {
            return new Verdict(Fire: false, Clear: false, Value: null);
        }

        var band = Math.Max(sensitivity * baseline.StdDev, MinimumDeviation(metric));
        var deviation = op == AlertOperator.Above ? average - baseline.Mean : baseline.Mean - average;
        var fresh = stats.LastTime >= now - tolerance;
        var covered = stats.FirstTime <= now - duration + tolerance;

        return new Verdict(
            Fire: fresh && covered && deviation > band,
            Clear: fresh && deviation <= band * AnomalyClearFraction,
            Value: average,
            Baseline: baseline.Mean);
    }

    /// <summary>The smallest departure from the usual level that counts as unusual, however steady the metric.</summary>
    public static double MinimumDeviation(AlertMetric metric) => metric switch
    {
        _ when metric.IsPercentage() => 5,
        AlertMetric.LoadPerCore => 0.25,
        AlertMetric.NetworkReceive or AlertMetric.NetworkTransmit => 1024 * 1024,
        _ => 0,
    };

    public static string Title(AlertRule rule, string hostName, string resourceKey)
    {
        if (rule.Metric == AlertMetric.HostOffline)
        {
            return $"{hostName} is offline";
        }

        if (rule.Metric == AlertMetric.ServiceFailed)
        {
            return $"{resourceKey} failed on {hostName}";
        }

        if (rule.Metric == AlertMetric.ContainerDown)
        {
            return $"{resourceKey} is down on {hostName}";
        }

        if (rule.Metric == AlertMetric.ContainerRestarts)
        {
            return $"{resourceKey} keeps restarting on {hostName}";
        }

        if (rule.Condition == AlertCondition.Anomaly)
        {
            return $"{rule.Metric.Label()} on {hostName} unusually {(rule.Operator == AlertOperator.Above ? "high" : "low")}";
        }

        var subject = rule.Metric.IsPerFilesystem() && resourceKey.Length > 0
            ? $"{rule.Metric.Label()} on {hostName} {resourceKey}"
            : $"{rule.Metric.Label()} on {hostName}";
        var direction = rule.Operator == AlertOperator.Above ? "above" : "below";
        return $"{subject} {direction} {FormatThreshold(rule.Metric, rule.Threshold)}";
    }

    public static string FormatThreshold(AlertMetric metric, double threshold) => metric switch
    {
        _ when metric.IsPercentage() => threshold.ToString("0.#", CultureInfo.InvariantCulture) + "%",
        AlertMetric.NetworkReceive or AlertMetric.NetworkTransmit => FormatRate(threshold),
        AlertMetric.ContainerRestarts => threshold == 1 ? "1 restart" : threshold.ToString("0", CultureInfo.InvariantCulture) + " restarts",
        _ => threshold.ToString("0.##", CultureInfo.InvariantCulture),
    };

    private static string FormatRate(double bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];
        var value = bytesPerSecond;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
