namespace Argus.Server.Features.Metrics;

/// <summary>Where a time series is read from: raw samples or one of the continuous aggregates.</summary>
public enum SeriesSource
{
    Raw,
    FiveMinutes,
    Hourly,
}

/// <summary>A validated time range for series queries.</summary>
public sealed record SeriesRange(DateTimeOffset From, DateTimeOffset To, int Points)
{
    public const int DefaultPoints = 300;
    public static readonly TimeSpan DefaultSpan = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(731);

    /// <summary>Fills in defaults (the last hour, 300 points) and returns validation errors, if any.</summary>
    public static Dictionary<string, string[]>? TryCreate(
        DateTimeOffset? from, DateTimeOffset? to, int? points, DateTimeOffset now, out SeriesRange range)
    {
        var end = (to ?? now).ToUniversalTime();
        var start = (from ?? end - DefaultSpan).ToUniversalTime();
        var count = points ?? DefaultPoints;
        range = new SeriesRange(start, end, count);

        var errors = new Dictionary<string, string[]>();
        if (start >= end)
        {
            errors["from"] = ["'from' must be earlier than 'to'."];
        }
        else if (end - start > MaxSpan)
        {
            errors["from"] = ["A range may cover at most two years."];
        }

        if (count is < 10 or > 2000)
        {
            errors["points"] = ["'points' must be between 10 and 2000."];
        }

        return errors.Count == 0 ? null : errors;
    }
}

public static class SeriesResolution
{
    private static readonly TimeSpan[] NiceBuckets =
    [
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(2), TimeSpan.FromDays(7),
    ];

    /// <summary>
    /// Picks the cheapest source that still resolves <paramref name="range"/> into roughly
    /// <paramref name="points"/> buckets, and a round bucket size. Raw buckets are at least two
    /// collection intervals wide so that sampling jitter does not leave every other bucket empty.
    /// </summary>
    public static (SeriesSource Source, TimeSpan Bucket) Choose(TimeSpan range, int points, TimeSpan collectionInterval)
    {
        var source = range <= TimeSpan.FromHours(6) ? SeriesSource.Raw
            : range <= TimeSpan.FromDays(7) ? SeriesSource.FiveMinutes
            : SeriesSource.Hourly;

        return (source, Bucket(range, points, Minimum(source, collectionInterval)));
    }

    /// <summary>Like <see cref="Choose"/> for series that only have raw data and an hourly rollup.</summary>
    public static (SeriesSource Source, TimeSpan Bucket) ChooseRawOrHourly(TimeSpan range, int points, TimeSpan collectionInterval)
    {
        var source = range <= TimeSpan.FromDays(2) ? SeriesSource.Raw : SeriesSource.Hourly;
        return (source, Bucket(range, points, Minimum(source, collectionInterval)));
    }

    public static string Label(this SeriesSource source) => source switch
    {
        SeriesSource.Raw => "raw",
        SeriesSource.FiveMinutes => "5m",
        _ => "1h",
    };

    private static TimeSpan Minimum(SeriesSource source, TimeSpan collectionInterval) => source switch
    {
        SeriesSource.Raw => collectionInterval * 2,
        SeriesSource.FiveMinutes => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromHours(1),
    };

    private static TimeSpan Bucket(TimeSpan range, int points, TimeSpan minimum)
    {
        var ideal = range / Math.Max(1, points);
        var target = ideal > minimum ? ideal : minimum;
        return NiceBuckets.FirstOrDefault(bucket => bucket >= target, NiceBuckets[^1]);
    }
}
