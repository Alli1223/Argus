namespace Argus.Server.Features.Summary;

/// <summary>How long each period of a usage summary is.</summary>
public enum SummaryPeriod
{
    Day,
    Month,
    Year,
}

/// <summary>
/// What the visible hosts did over the last few days, months or years. Periods are newest first and
/// follow the calendar of <see cref="TimeZone"/>; <see cref="HistoryDays"/> is how far back hourly
/// rollups, which the summary is built from, are kept.
/// </summary>
public sealed record UsageSummary(
    SummaryPeriod Period,
    string TimeZone,
    int HistoryDays,
    IReadOnlyList<SummaryHost> Hosts,
    IReadOnlyList<UsagePeriod> Periods);

public sealed record SummaryHost(Guid HostId, string DisplayName);

/// <summary>
/// One period: the whole fleet's figures and each host's, for the hosts that reported or alerted in it,
/// and the internet traffic of the UniFi networks the hosts read (null when none reported in it).
/// </summary>
public sealed record UsagePeriod(
    DateTimeOffset Start, DateTimeOffset End, UsageStats Totals, IReadOnlyList<HostUsage> Hosts, InternetUsage? Internet = null);

/// <summary>Bytes that came in from and went out to the internet, through UniFi gateways.</summary>
public sealed record InternetUsage(double DownloadedBytes, double UploadedBytes);

public sealed record HostUsage(Guid HostId, UsageStats Stats);

/// <summary>
/// Figures for one host or the fleet over one period. Byte counts are worked out from the average
/// throughput of each hour and how long the host reported in it. Figures with no readings are null.
/// </summary>
/// <param name="ReportingSeconds">How long the host sent readings, summed across hosts for the fleet.</param>
/// <param name="AvailabilityPercent">
/// The share of the period, so far and since the host was added, that it sent readings; the fleet's is the
/// average of its hosts'.
/// </param>
public sealed record UsageStats(
    double? DiskReadBytes,
    double? DiskWriteBytes,
    double? NetworkReceivedBytes,
    double? NetworkSentBytes,
    double? CpuAveragePercent,
    double? CpuPeakPercent,
    double? MemoryAveragePercent,
    double? MemoryPeakPercent,
    double? ReportingSeconds,
    double? AvailabilityPercent,
    double? TemperaturePeakCelsius,
    int Alerts,
    int CriticalAlerts);
