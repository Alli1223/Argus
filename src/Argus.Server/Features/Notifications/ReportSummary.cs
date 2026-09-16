using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Alerts;
using Argus.Server.Features.Hosts;
using Argus.Server.Features.Metrics;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Argus.Server.Features.Notifications;

/// <summary>A summary of one person's hosts and alerts over a report period, captured when the report was queued.</summary>
public sealed record ReportSummary(
    ReportKind Kind,
    DateTimeOffset From,
    DateTimeOffset To,
    int HostCount,
    int OfflineHostCount,
    AlertTally AlertsRaised,
    int FiringCount,
    IReadOnlyList<ReportAlert> Firing,
    IReadOnlyList<ReportHost> Hosts,
    IReadOnlyList<ReportServiceFailure> FailedServices)
{
    /// <summary>The most alerts, hosts or services a report lists; the counts still cover them all.</summary>
    public const int MaxListed = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ReportSummary FromJson(string json) =>
        JsonSerializer.Deserialize<ReportSummary>(json, Json) ?? throw new JsonException("The report is empty.");
}

public sealed record AlertTally(int Critical, int Warning, int Info)
{
    [JsonIgnore]
    public int Total => Critical + Warning + Info;
}

public sealed record ReportAlert(string Title, AlertSeverity Severity, string HostName, DateTimeOffset FiredAt);

/// <param name="Online">Whether the host was reporting when the report was made.</param>
/// <param name="FullestDiskPercent">The fullest filesystem when the report was made.</param>
public sealed record ReportHost(
    string Name,
    bool Online,
    double? CpuAverage,
    double? CpuPeak,
    double? MemoryAverage,
    double? FullestDiskPercent,
    int AlertsRaised);

public sealed record ReportServiceFailure(string HostName, string Service, DateTimeOffset Since);

/// <summary>Gathers what a report says: alerts raised in the period, averages from the hourly rollup, and the state now.</summary>
public sealed class ReportBuilder(ArgusDbContext db, NpgsqlDataSource dataSource, TimeSeriesQueries series, IOptions<AgentOptions> agents)
{
    public async Task<ReportSummary> BuildAsync(
        Guid ownerId, ReportKind kind, DateTimeOffset to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var from = to - ReportSchedule.Length(kind);
        var hosts = await db.Hosts.AsNoTracking()
            .Where(host => host.OwnerId == ownerId)
            .Select(host => new { host.Id, host.DisplayName, host.LastSeenAt })
            .ToListAsync(cancellationToken);
        var hostIds = hosts.Select(host => host.Id).ToArray();

        var raised = await db.Alerts.AsNoTracking()
            .Where(alert => alert.OwnerId == ownerId && alert.FiredAt >= from && alert.FiredAt < to)
            .GroupBy(alert => new { alert.HostId, alert.Severity })
            .Select(group => new { group.Key.HostId, group.Key.Severity, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var firing = (await db.Alerts.AsNoTracking()
                .Where(alert => alert.OwnerId == ownerId && alert.Status == AlertStatus.Firing)
                .Select(alert => new ReportAlert(alert.Title, alert.Severity, alert.Host!.DisplayName, alert.FiredAt))
                .ToListAsync(cancellationToken))
            .OrderByDescending(alert => alert.Severity)
            .ThenBy(alert => alert.FiredAt)
            .ToList();

        Dictionary<Guid, UsageRow> usage;
        List<ServiceRow> failures;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            usage = (await connection.QueryAsync<UsageRow>(new CommandDefinition("""
                    SELECT host_id,
                           avg(cpu_usage_pct)                                     AS cpu_average,
                           max(cpu_usage_pct_max)                                 AS cpu_peak,
                           avg(mem_used_bytes / NULLIF(mem_total_bytes, 0) * 100) AS memory_average
                    FROM host_metrics_1h
                    WHERE host_id = ANY(@host_ids) AND bucket >= @from AND bucket < @to
                    GROUP BY host_id
                    """,
                    new { host_ids = hostIds, from, to },
                    cancellationToken: cancellationToken)))
                .ToDictionary(row => row.HostId);

            failures = [.. await connection.QueryAsync<ServiceRow>(new CommandDefinition(
                "SELECT host_id, service, since FROM host_service_failures WHERE host_id = ANY(@host_ids) ORDER BY since, service",
                new { host_ids = hostIds },
                cancellationToken: cancellationToken))];
        }

        var latest = await series.GetLatestAsync(hostIds, cancellationToken);
        var names = hosts.ToDictionary(host => host.Id, host => host.DisplayName);

        // Hosts that need a look come first: offline, then those with the most alerts.
        var reportHosts = hosts
            .Select(host => new ReportHost(
                host.DisplayName,
                HostAccess.StatusOf(host.LastSeenAt, now, agents.Value) == HostStatus.Online,
                usage.GetValueOrDefault(host.Id)?.CpuAverage,
                usage.GetValueOrDefault(host.Id)?.CpuPeak,
                usage.GetValueOrDefault(host.Id)?.MemoryAverage,
                latest.GetValueOrDefault(host.Id)?.DiskUsedPercent,
                raised.Where(row => row.HostId == host.Id).Sum(row => row.Count)))
            .OrderBy(host => host.Online)
            .ThenByDescending(host => host.AlertsRaised)
            .ThenBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int Raised(AlertSeverity severity) => raised.Where(row => row.Severity == severity).Sum(row => row.Count);

        return new ReportSummary(
            kind,
            from,
            to,
            reportHosts.Count,
            reportHosts.Count(host => !host.Online),
            new AlertTally(Raised(AlertSeverity.Critical), Raised(AlertSeverity.Warning), Raised(AlertSeverity.Info)),
            firing.Count,
            [.. firing.Take(ReportSummary.MaxListed)],
            [.. reportHosts.Take(ReportSummary.MaxListed)],
            [.. failures.Take(ReportSummary.MaxListed).Select(row => new ReportServiceFailure(
                names.GetValueOrDefault(row.HostId, "Unknown host"),
                row.Service,
                new DateTimeOffset(DateTime.SpecifyKind(row.Since, DateTimeKind.Utc))))]);
    }

    private sealed class UsageRow
    {
        public Guid HostId { get; set; }
        public double? CpuAverage { get; set; }
        public double? CpuPeak { get; set; }
        public double? MemoryAverage { get; set; }
    }

    private sealed class ServiceRow
    {
        public Guid HostId { get; set; }
        public string Service { get; set; } = "";
        public DateTime Since { get; set; }
    }
}

/// <summary>The phrases reports are made of, shared by every kind of channel.</summary>
public static class ReportText
{
    public static string Title(ReportSummary report) =>
        $"{(report.Kind == ReportKind.Daily ? "Daily" : "Weekly")} report, {Dates(report)}";

    /// <summary>The report's days: "16 Sep 2026" for a daily report, "9–16 Sep 2026" for a weekly one.</summary>
    public static string Dates(ReportSummary report)
    {
        var (from, to) = (report.From.UtcDateTime, report.To.UtcDateTime);
        if (report.Kind == ReportKind.Daily)
        {
            return Format(to, "d MMM yyyy");
        }

        return (from.Year == to.Year, from.Month == to.Month) switch
        {
            (true, true) => $"{Format(from, "%d")}–{Format(to, "d MMM yyyy")}",
            (true, false) => $"{Format(from, "d MMM")} – {Format(to, "d MMM yyyy")}",
            _ => $"{Format(from, "d MMM yyyy")} – {Format(to, "d MMM yyyy")}",
        };
    }

    /// <summary>The exact period: "15 Sep 2026, 07:00 to 16 Sep 2026, 07:00 UTC".</summary>
    public static string Period(ReportSummary report) =>
        $"{Format(report.From.UtcDateTime, "d MMM yyyy, HH:mm")} to {Format(report.To.UtcDateTime, "d MMM yyyy, HH:mm")} UTC";

    /// <summary>What needs attention now, or that nothing does: "2 alerts firing, 1 host offline".</summary>
    public static string Headline(ReportSummary report)
    {
        List<string> parts = [];
        if (report.FiringCount > 0)
        {
            parts.Add(Count(report.FiringCount, "alert", "alerts") + " firing");
        }

        if (report.OfflineHostCount > 0)
        {
            parts.Add(Count(report.OfflineHostCount, "host", "hosts") + " offline");
        }

        if (report.FailedServices.Count > 0)
        {
            parts.Add(Count(report.FailedServices.Count, "failed service", "failed services"));
        }

        return parts.Count == 0 ? "All quiet" : string.Join(", ", parts);
    }

    public static string Hosts(ReportSummary report) =>
        report.OfflineHostCount > 0 ? $"{report.HostCount}, {report.OfflineHostCount} offline" : $"{report.HostCount}, all online";

    /// <summary>"4 (1 critical, 3 warning)" or "None".</summary>
    public static string Raised(AlertTally tally)
    {
        if (tally.Total == 0)
        {
            return "None";
        }

        List<string> parts = [];
        if (tally.Critical > 0)
        {
            parts.Add($"{tally.Critical} critical");
        }

        if (tally.Warning > 0)
        {
            parts.Add($"{tally.Warning} warning");
        }

        if (tally.Info > 0)
        {
            parts.Add($"{tally.Info} info");
        }

        return $"{tally.Total} ({string.Join(", ", parts)})";
    }

    public static string Percent(double? value) =>
        value is { } number ? number.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "–";

    public static string Time(DateTimeOffset time) => Format(time.UtcDateTime, "d MMM, HH:mm 'UTC'");

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string Format(DateTime time, string format) => time.ToString(format, CultureInfo.InvariantCulture);
}
