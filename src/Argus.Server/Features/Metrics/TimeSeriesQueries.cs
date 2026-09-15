using System.Text.Json;
using Argus.Contracts.Agent;
using Argus.Server.Features.Hosts;
using Dapper;
using Npgsql;

namespace Argus.Server.Features.Metrics;

/// <summary>Columnar time series (the shape chart libraries such as uPlot consume). Missing buckets are null.</summary>
public sealed record MetricSeries(
    DateTimeOffset From,
    DateTimeOffset To,
    string Resolution,
    int BucketSeconds,
    IReadOnlyList<long> Time,
    IReadOnlyDictionary<string, double?[]> Series);

public sealed record FilesystemSnapshot(
    string MountPoint,
    string? Device,
    string? FsType,
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    double UsedPercent,
    long? InodesTotal,
    long? InodesUsed,
    DateTimeOffset Time);

public sealed record ProcessSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<ProcessMetrics> Processes);

/// <summary>A host's services: when the agent last checked (null if never) and what was failing then.</summary>
public sealed record ServiceStatus(DateTimeOffset? CheckedAt, IReadOnlyList<ServiceFailure> Failures);

/// <param name="Since">When the failure was first seen; it keeps this time until the service recovers.</param>
public sealed record ServiceFailure(string Service, string? Description, string State, DateTimeOffset Since);

/// <summary>
/// Reads time series from TimescaleDB with raw SQL (Dapper). Callers check that the user may see
/// the host before asking; these queries only filter by host id.
/// </summary>
public sealed class TimeSeriesQueries(NpgsqlDataSource dataSource)
{
    private const string HostSeriesColumns = """
        avg(cpu_usage_pct)::float8     AS cpu,
        max({0})::float8               AS cpu_max,
        avg(cpu_iowait_pct)::float8    AS iowait,
        avg(mem_used_bytes)::float8    AS mem_used,
        max(mem_total_bytes)::float8   AS mem_total,
        avg(swap_used_bytes)::float8   AS swap_used,
        max(swap_total_bytes)::float8  AS swap_total,
        avg(load_1)::float8            AS load_1,
        avg(load_5)::float8            AS load_5,
        avg(load_15)::float8           AS load_15,
        avg(disk_read_bps)::float8     AS disk_read,
        avg(disk_write_bps)::float8    AS disk_write,
        avg(disk_util_pct)::float8     AS disk_util,
        avg(net_rx_bps)::float8        AS net_rx,
        avg(net_tx_bps)::float8        AS net_tx,
        avg(process_count)::float8     AS processes
        """;

    static TimeSeriesQueries() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    /// <summary>The newest sample of each host from the last day, plus the fullest filesystem.</summary>
    public async Task<Dictionary<Guid, LatestMetrics>> GetLatestAsync(IReadOnlyCollection<Guid> hostIds, CancellationToken cancellationToken)
    {
        if (hostIds.Count == 0)
        {
            return [];
        }

        var parameters = new { ids = hostIds.ToArray() };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var samples = await connection.QueryAsync<LatestRow>(new CommandDefinition("""
            SELECT h.id AS host_id, m.time, m.cpu_usage_pct::float8 AS cpu, m.mem_used_bytes AS mem_used,
                   m.mem_total_bytes AS mem_total, m.swap_used_bytes AS swap_used, m.swap_total_bytes AS swap_total,
                   m.load_1::float8 AS load_1, m.net_rx_bps AS net_rx, m.net_tx_bps AS net_tx, m.uptime_seconds AS uptime
            FROM unnest(@ids) AS h(id)
            CROSS JOIN LATERAL (
                SELECT * FROM host_metrics
                WHERE host_id = h.id AND time > now() - interval '1 day'
                ORDER BY time DESC
                LIMIT 1) m
            """, parameters, cancellationToken: cancellationToken));

        var disks = (await connection.QueryAsync<DiskRow>(new CommandDefinition("""
            SELECT h.id AS host_id, max(f.used_bytes::float8 / NULLIF(f.used_bytes + f.available_bytes, 0) * 100) AS used_percent
            FROM unnest(@ids) AS h(id)
            CROSS JOIN LATERAL (
                SELECT used_bytes, available_bytes FROM filesystem_metrics
                WHERE host_id = h.id
                  AND time = (SELECT max(time) FROM filesystem_metrics WHERE host_id = h.id AND time > now() - interval '1 day')
            ) f
            GROUP BY h.id
            """, parameters, cancellationToken: cancellationToken))).ToDictionary(row => row.HostId, row => row.UsedPercent);

        return samples.ToDictionary(row => row.HostId, row => new LatestMetrics(
            Utc(row.Time),
            row.Cpu,
            row.MemTotal > 0 ? 100.0 * row.MemUsed / row.MemTotal : 0,
            row.MemUsed,
            row.MemTotal,
            row.SwapTotal > 0 ? 100.0 * row.SwapUsed / row.SwapTotal : null,
            row.Load1,
            disks.GetValueOrDefault(row.HostId),
            row.NetRx,
            row.NetTx,
            row.Uptime));
    }

    public async Task<MetricSeries> GetHostSeriesAsync(Guid hostId, SeriesRange range, TimeSpan collectionInterval, CancellationToken cancellationToken)
    {
        var (source, bucket) = SeriesResolution.Choose(range.To - range.From, range.Points, collectionInterval);
        var sql = source == SeriesSource.Raw
            ? $"""
               SELECT time_bucket_gapfill(@bucket, time, @from, @to) AS bucket,
               {string.Format(HostSeriesColumns, "cpu_usage_pct")}
               FROM host_metrics
               WHERE host_id = @host_id AND time >= @from AND time < @to
               GROUP BY 1 ORDER BY 1
               """
            : $"""
               SELECT time_bucket_gapfill(@bucket, bucket, @from, @to) AS bucket,
               {string.Format(HostSeriesColumns, "cpu_usage_pct_max")}
               FROM {(source == SeriesSource.FiveMinutes ? "host_metrics_5m" : "host_metrics_1h")}
               WHERE host_id = @host_id AND bucket >= @from AND bucket < @to
               GROUP BY 1 ORDER BY 1
               """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<HostSeriesRow>(new CommandDefinition(
            sql, Parameters(hostId, range, bucket), cancellationToken: cancellationToken))).ToList();

        return new MetricSeries(range.From, range.To, source.Label(), (int)bucket.TotalSeconds,
            rows.Select(row => Unix(row.Bucket)).ToList(),
            new Dictionary<string, double?[]>
            {
                ["cpu"] = [.. rows.Select(r => r.Cpu)],
                ["cpuMax"] = [.. rows.Select(r => r.CpuMax)],
                ["iowait"] = [.. rows.Select(r => r.Iowait)],
                ["memUsed"] = [.. rows.Select(r => r.MemUsed)],
                ["memTotal"] = [.. rows.Select(r => r.MemTotal)],
                ["swapUsed"] = [.. rows.Select(r => r.SwapUsed)],
                ["swapTotal"] = [.. rows.Select(r => r.SwapTotal)],
                ["load1"] = [.. rows.Select(r => r.Load1)],
                ["load5"] = [.. rows.Select(r => r.Load5)],
                ["load15"] = [.. rows.Select(r => r.Load15)],
                ["diskRead"] = [.. rows.Select(r => r.DiskRead)],
                ["diskWrite"] = [.. rows.Select(r => r.DiskWrite)],
                ["diskUtil"] = [.. rows.Select(r => r.DiskUtil)],
                ["netRx"] = [.. rows.Select(r => r.NetRx)],
                ["netTx"] = [.. rows.Select(r => r.NetTx)],
                ["processes"] = [.. rows.Select(r => r.Processes)],
            });
    }

    /// <summary>The filesystems in the host's newest sample.</summary>
    public async Task<List<FilesystemSnapshot>> GetFilesystemsAsync(Guid hostId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<FilesystemRow>(new CommandDefinition("""
            SELECT mount_point, device, fs_type, total_bytes, used_bytes, available_bytes, inodes_total, inodes_used, time
            FROM filesystem_metrics
            WHERE host_id = @host_id
              AND time = (SELECT max(time) FROM filesystem_metrics WHERE host_id = @host_id AND time > now() - interval '1 day')
            ORDER BY mount_point
            """, new { host_id = hostId }, cancellationToken: cancellationToken));

        return rows.Select(row => new FilesystemSnapshot(
            row.MountPoint, row.Device, row.FsType, row.TotalBytes, row.UsedBytes, row.AvailableBytes,
            row.UsedBytes + row.AvailableBytes > 0 ? 100.0 * row.UsedBytes / (row.UsedBytes + row.AvailableBytes) : 0,
            row.InodesTotal, row.InodesUsed, Utc(row.Time))).ToList();
    }

    /// <summary>Used percentage of each filesystem over time; series are keyed by mount point.</summary>
    public async Task<MetricSeries> GetFilesystemHistoryAsync(Guid hostId, SeriesRange range, TimeSpan collectionInterval, CancellationToken cancellationToken)
    {
        var (source, bucket) = SeriesResolution.ChooseRawOrHourly(range.To - range.From, range.Points, collectionInterval);
        var sql = source == SeriesSource.Raw
            ? """
              SELECT time_bucket_gapfill(@bucket, time, @from, @to) AS bucket, mount_point AS key,
                     max(used_bytes::float8 / NULLIF(used_bytes + available_bytes, 0) * 100) AS value
              FROM filesystem_metrics
              WHERE host_id = @host_id AND time >= @from AND time < @to
              GROUP BY 1, 2 ORDER BY 1
              """
            : """
              SELECT time_bucket_gapfill(@bucket, bucket, @from, @to) AS bucket, mount_point AS key,
                     max(used_bytes / NULLIF(used_bytes + available_bytes, 0) * 100) AS value
              FROM filesystem_metrics_1h
              WHERE host_id = @host_id AND bucket >= @from AND bucket < @to
              GROUP BY 1, 2 ORDER BY 1
              """;

        return await PivotAsync(sql, hostId, range, source, bucket, cancellationToken);
    }

    /// <summary>Per-interface traffic over time; series are keyed <c>rx:{interface}</c> and <c>tx:{interface}</c>.</summary>
    public async Task<MetricSeries> GetNetworkHistoryAsync(Guid hostId, SeriesRange range, TimeSpan collectionInterval, CancellationToken cancellationToken)
    {
        var (source, bucket) = SeriesResolution.ChooseRawOrHourly(range.To - range.From, range.Points, collectionInterval);
        var (table, timeColumn) = source == SeriesSource.Raw ? ("network_metrics", "time") : ("network_metrics_1h", "bucket");
        var sql = $"""
            WITH traffic AS (
                SELECT time_bucket_gapfill(@bucket, {timeColumn}, @from, @to) AS bucket, interface,
                       avg(rx_bps)::float8 AS rx, avg(tx_bps)::float8 AS tx
                FROM {table}
                WHERE host_id = @host_id AND {timeColumn} >= @from AND {timeColumn} < @to
                GROUP BY 1, 2)
            SELECT bucket, 'rx:' || interface AS key, rx AS value FROM traffic
            UNION ALL
            SELECT bucket, 'tx:' || interface AS key, tx AS value FROM traffic
            ORDER BY 1
            """;

        return await PivotAsync(sql, hostId, range, source, bucket, cancellationToken);
    }

    public async Task<ProcessSnapshot?> GetProcessesAsync(Guid hostId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ProcessRow>(new CommandDefinition(
            "SELECT captured_at, processes::text AS processes FROM host_processes WHERE host_id = @host_id",
            new { host_id = hostId }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new ProcessSnapshot(
                Utc(row.CapturedAt),
                JsonSerializer.Deserialize<List<ProcessMetrics>>(row.Processes, JsonSerializerOptions.Web) ?? []);
    }

    public async Task<ServiceStatus> GetServicesAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var parameters = new { host_id = hostId };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var checkedAt = await connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
            "SELECT checked_at FROM host_service_checks WHERE host_id = @host_id", parameters, cancellationToken: cancellationToken));
        var failures = await connection.QueryAsync<ServiceFailureRow>(new CommandDefinition(
            "SELECT service, description, state, since FROM host_service_failures WHERE host_id = @host_id ORDER BY since, service",
            parameters, cancellationToken: cancellationToken));

        return new ServiceStatus(
            checkedAt is { } at ? Utc(at) : null,
            failures.Select(row => new ServiceFailure(row.Service, row.Description, row.State, Utc(row.Since))).ToList());
    }

    /// <summary>Removes a deleted host's samples (its rollup buckets simply age out).</summary>
    public async Task DeleteHostDataAsync(Guid hostId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM host_metrics WHERE host_id = @host_id;
            DELETE FROM filesystem_metrics WHERE host_id = @host_id;
            DELETE FROM network_metrics WHERE host_id = @host_id;
            """, new { host_id = hostId }, cancellationToken: cancellationToken));
    }

    private async Task<MetricSeries> PivotAsync(
        string sql, Guid hostId, SeriesRange range, SeriesSource source, TimeSpan bucket, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<KeyedRow>(new CommandDefinition(
            sql, Parameters(hostId, range, bucket), cancellationToken: cancellationToken))).ToList();

        var times = rows.Select(row => row.Bucket).Distinct().Order().ToList();
        var index = times.Select((time, position) => (time, position)).ToDictionary(pair => pair.time, pair => pair.position);
        var series = new Dictionary<string, double?[]>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!series.TryGetValue(row.Key, out var values))
            {
                series[row.Key] = values = new double?[times.Count];
            }

            values[index[row.Bucket]] = row.Value;
        }

        return new MetricSeries(range.From, range.To, source.Label(), (int)bucket.TotalSeconds, times.Select(Unix).ToList(), series);
    }

    private static object Parameters(Guid hostId, SeriesRange range, TimeSpan bucket) =>
        new { host_id = hostId, bucket, from = range.From, to = range.To };

    private static long Unix(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static DateTimeOffset Utc(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private sealed class LatestRow
    {
        public Guid HostId { get; set; }
        public DateTime Time { get; set; }
        public double Cpu { get; set; }
        public long MemUsed { get; set; }
        public long MemTotal { get; set; }
        public long SwapUsed { get; set; }
        public long SwapTotal { get; set; }
        public double? Load1 { get; set; }
        public double? NetRx { get; set; }
        public double? NetTx { get; set; }
        public long Uptime { get; set; }
    }

    private sealed class DiskRow
    {
        public Guid HostId { get; set; }
        public double? UsedPercent { get; set; }
    }

    private sealed class HostSeriesRow
    {
        public DateTime Bucket { get; set; }
        public double? Cpu { get; set; }
        public double? CpuMax { get; set; }
        public double? Iowait { get; set; }
        public double? MemUsed { get; set; }
        public double? MemTotal { get; set; }
        public double? SwapUsed { get; set; }
        public double? SwapTotal { get; set; }
        public double? Load1 { get; set; }
        public double? Load5 { get; set; }
        public double? Load15 { get; set; }
        public double? DiskRead { get; set; }
        public double? DiskWrite { get; set; }
        public double? DiskUtil { get; set; }
        public double? NetRx { get; set; }
        public double? NetTx { get; set; }
        public double? Processes { get; set; }
    }

    private sealed class FilesystemRow
    {
        public string MountPoint { get; set; } = "";
        public string? Device { get; set; }
        public string? FsType { get; set; }
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public long? InodesTotal { get; set; }
        public long? InodesUsed { get; set; }
        public DateTime Time { get; set; }
    }

    private sealed class KeyedRow
    {
        public DateTime Bucket { get; set; }
        public string Key { get; set; } = "";
        public double? Value { get; set; }
    }

    private sealed class ProcessRow
    {
        public DateTime CapturedAt { get; set; }
        public string Processes { get; set; } = "[]";
    }

    private sealed class ServiceFailureRow
    {
        public string Service { get; set; } = "";
        public string? Description { get; set; }
        public string State { get; set; } = "";
        public DateTime Since { get; set; }
    }
}
