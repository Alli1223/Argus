using System.ComponentModel.DataAnnotations;

namespace Argus.Server.Features.Metrics;

/// <summary>How long time-series data is kept, bound from <c>Argus:Retention</c>.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Argus:Retention";

    /// <summary>
    /// Days of full-resolution samples. Must stay longer than the rollups' 3-day refresh window,
    /// otherwise refreshing a rollup over deleted raw data would erase it.
    /// </summary>
    [Range(7, 3650)]
    public int RawDays { get; set; } = 14;

    /// <summary>Days of 5-minute rollups.</summary>
    [Range(7, 3650)]
    public int FiveMinuteDays { get; set; } = 90;

    /// <summary>Days of hourly rollups.</summary>
    [Range(30, 36_500)]
    public int HourlyDays { get; set; } = 730;
}

/// <summary>Sanity limits for incoming samples, bound from <c>Argus:Ingest</c>.</summary>
public sealed class IngestOptions
{
    public const string SectionName = "Argus:Ingest";

    /// <summary>Oldest sample accepted, e.g. from an agent that buffered through an outage.</summary>
    [Range(1, 336)]
    public int MaxSampleAgeHours { get; set; } = 72;

    /// <summary>How far ahead of the server clock a sample may be (agent clock skew).</summary>
    [Range(0, 3600)]
    public int MaxClockSkewSeconds { get; set; } = 300;
}
