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

/// <summary>Whether a rule's condition is met (fire), clearly over (clear), or neither.</summary>
public readonly record struct Verdict(bool Fire, bool Clear, double? Value);

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
