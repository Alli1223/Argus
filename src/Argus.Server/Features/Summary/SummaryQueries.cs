using Dapper;
using Npgsql;

namespace Argus.Server.Features.Summary;

/// <summary>
/// Adds up hourly rollups and alerts by calendar period with raw SQL (Dapper). Callers check that the
/// user may see the hosts before asking. The database works out the periods, since it has the time
/// zone rules and the server's image does not.
/// </summary>
public sealed class SummaryQueries(NpgsqlDataSource dataSource)
{
    public async Task<bool> IsTimeZoneAsync(string timeZone, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = @time_zone)",
            new { time_zone = timeZone }, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// The current period and the <paramref name="count"/> - 1 before it, in <paramref name="timeZone"/>.
    /// Byte counts take each hour's average throughput over the time the host reported in it, at most the hour.
    /// </summary>
    public async Task<List<UsagePeriod>> GetAsync(
        IReadOnlyDictionary<Guid, DateTimeOffset> hosts,
        SummaryPeriod period,
        int count,
        string timeZone,
        TimeSpan collectionInterval,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var unit = period switch
        {
            SummaryPeriod.Day => "day",
            SummaryPeriod.Month => "month",
            _ => "year",
        };

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var periods = (await connection.QueryAsync<PeriodRow>(new CommandDefinition("""
            SELECT n AS index,
                   local_start AT TIME ZONE @time_zone AS start,
                   (local_start + make_interval(days => @days, months => @months, years => @years)) AT TIME ZONE @time_zone AS finish
            FROM generate_series(0, @count - 1) AS n
            CROSS JOIN LATERAL (
                SELECT date_trunc(@unit, @now AT TIME ZONE @time_zone)
                       - n * make_interval(days => @days, months => @months, years => @years) AS local_start) s
            ORDER BY n
            """,
            new
            {
                time_zone = timeZone,
                unit,
                count,
                now = now.UtcDateTime,
                days = period == SummaryPeriod.Day ? 1 : 0,
                months = period == SummaryPeriod.Month ? 1 : 0,
                years = period == SummaryPeriod.Year ? 1 : 0,
            },
            cancellationToken: cancellationToken)))
            .Select(row => new PeriodBounds(row.Index, Utc(row.Start), Utc(row.Finish)))
            .ToList();

        if (hosts.Count == 0 || periods.Count == 0)
        {
            return SummaryMerge.Merge(periods, hosts, [], [], [], now);
        }

        var parameters = new
        {
            host_ids = hosts.Keys.ToArray(),
            indexes = periods.Select(p => p.Index).ToArray(),
            starts = periods.Select(p => p.Start.UtcDateTime).ToArray(),
            ends = periods.Select(p => p.End.UtcDateTime).ToArray(),
            from = periods.Min(p => p.Start).UtcDateTime,
            to = periods.Max(p => p.End).UtcDateTime,
            interval = (int)collectionInterval.TotalSeconds,
        };

        const string Periods = "unnest(@indexes, @starts, @ends) AS p(n, start, finish)";

        var metrics = await connection.QueryAsync<MetricsRow>(new CommandDefinition($"""
            WITH hourly AS (
                SELECT h.*, p.n, least(h.samples * @interval, 3600)::float8 AS seconds
                FROM host_metrics_1h h
                JOIN {Periods} ON h.bucket >= p.start AND h.bucket < p.finish
                WHERE h.host_id = ANY(@host_ids) AND h.bucket >= @from AND h.bucket < @to)
            SELECT n AS period, host_id,
                   sum(samples)::float8                                                   AS samples,
                   sum(seconds)                                                           AS reporting_seconds,
                   sum(disk_read_bps * seconds)::float8                                   AS disk_read_bytes,
                   sum(disk_write_bps * seconds)::float8                                  AS disk_write_bytes,
                   sum(net_rx_bps * seconds)::float8                                      AS network_received_bytes,
                   sum(net_tx_bps * seconds)::float8                                      AS network_sent_bytes,
                   (sum(cpu_usage_pct * samples) / NULLIF(sum(samples), 0))::float8       AS cpu_average_percent,
                   max(cpu_usage_pct_max)::float8                                         AS cpu_peak_percent,
                   (sum(mem_used_bytes * samples) / NULLIF(sum(samples), 0)
                       / NULLIF(max(mem_total_bytes), 0) * 100)::float8                   AS memory_average_percent,
                   max(mem_used_bytes_max::float8 / NULLIF(mem_total_bytes, 0) * 100)     AS memory_peak_percent
            FROM hourly
            GROUP BY 1, 2
            """, parameters, cancellationToken: cancellationToken));

        var temperatures = await connection.QueryAsync<TemperatureRow>(new CommandDefinition($"""
            SELECT p.n AS period, t.host_id, max(t.celsius_max)::float8 AS peak_celsius
            FROM temperature_metrics_1h t
            JOIN {Periods} ON t.bucket >= p.start AND t.bucket < p.finish
            WHERE t.host_id = ANY(@host_ids) AND t.bucket >= @from AND t.bucket < @to
            GROUP BY 1, 2
            """, parameters, cancellationToken: cancellationToken));

        var alerts = await connection.QueryAsync<AlertRow>(new CommandDefinition($"""
            SELECT p.n AS period, a.host_id, count(*)::int AS alerts,
                   (count(*) FILTER (WHERE a.severity = 'Critical'))::int AS critical_alerts
            FROM alerts a
            JOIN {Periods} ON a.fired_at >= p.start AND a.fired_at < p.finish
            WHERE a.host_id = ANY(@host_ids) AND a.fired_at >= @from AND a.fired_at < @to
            GROUP BY 1, 2
            """, parameters, cancellationToken: cancellationToken));

        return SummaryMerge.Merge(
            periods,
            hosts,
            metrics.Select(row => new HostPeriodRow(
                row.Period, row.HostId, row.Samples, row.ReportingSeconds, row.DiskReadBytes, row.DiskWriteBytes,
                row.NetworkReceivedBytes, row.NetworkSentBytes, row.CpuAveragePercent, row.CpuPeakPercent,
                row.MemoryAveragePercent, row.MemoryPeakPercent)),
            temperatures.Select(row => new HostPeriodTemperature(row.Period, row.HostId, row.PeakCelsius)),
            alerts.Select(row => new HostPeriodAlerts(row.Period, row.HostId, row.Alerts, row.CriticalAlerts)),
            now);
    }

    private static DateTimeOffset Utc(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private sealed class PeriodRow
    {
        public int Index { get; set; }
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }
    }

    private sealed class MetricsRow
    {
        public int Period { get; set; }
        public Guid HostId { get; set; }
        public double Samples { get; set; }
        public double ReportingSeconds { get; set; }
        public double? DiskReadBytes { get; set; }
        public double? DiskWriteBytes { get; set; }
        public double? NetworkReceivedBytes { get; set; }
        public double? NetworkSentBytes { get; set; }
        public double? CpuAveragePercent { get; set; }
        public double? CpuPeakPercent { get; set; }
        public double? MemoryAveragePercent { get; set; }
        public double? MemoryPeakPercent { get; set; }
    }

    private sealed class TemperatureRow
    {
        public int Period { get; set; }
        public Guid HostId { get; set; }
        public double? PeakCelsius { get; set; }
    }

    private sealed class AlertRow
    {
        public int Period { get; set; }
        public Guid HostId { get; set; }
        public int Alerts { get; set; }
        public int CriticalAlerts { get; set; }
    }
}
