using Argus.Server.Features.Metrics;
using Dapper;
using Npgsql;

namespace Argus.Server.Features.Unifi;

/// <summary>
/// Reads UniFi networks with raw SQL (Dapper). Callers check that the user may see the hosts before
/// asking; these queries only filter by host id.
/// </summary>
public sealed class UnifiQueries(NpgsqlDataSource dataSource)
{
    /// <summary>The networks the hosts read, with their newest reading and devices; hosts that read none are left out.</summary>
    public async Task<List<UnifiNetworkResponse>> GetNetworksAsync(
        IReadOnlyDictionary<Guid, string> hosts, CancellationToken cancellationToken)
    {
        if (hosts.Count == 0)
        {
            return [];
        }

        var parameters = new { host_ids = hosts.Keys.ToArray() };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var checks = await connection.QueryAsync<CheckRow>(new CommandDefinition(
            "SELECT host_id, checked_at, site, version, problem FROM unifi_checks WHERE host_id = ANY(@host_ids)",
            parameters, cancellationToken: cancellationToken));

        var latest = (await connection.QueryAsync<NowRow>(new CommandDefinition("""
            SELECT h.id AS host_id, m.time, m.wan_rx_bps, m.wan_tx_bps, m.wired_clients, m.wireless_clients, m.guest_clients
            FROM unnest(@host_ids) AS h(id)
            CROSS JOIN LATERAL (
                SELECT * FROM unifi_metrics
                WHERE host_id = h.id AND time > now() - interval '1 day'
                ORDER BY time DESC
                LIMIT 1) m
            """, parameters, cancellationToken: cancellationToken))).ToDictionary(row => row.HostId);

        var devices = (await connection.QueryAsync<DeviceRow>(new CommandDefinition("""
            SELECT host_id, mac, name, model, type, online, uptime_seconds, cpu_pct::float8 AS cpu_pct, mem_pct::float8 AS mem_pct,
                   rx_bps, tx_bps, clients, seen_at, state_since
            FROM unifi_devices
            WHERE host_id = ANY(@host_ids)
            ORDER BY online, lower(name)
            """, parameters, cancellationToken: cancellationToken))).ToLookup(row => row.HostId);

        return checks
            .Where(check => hosts.ContainsKey(check.HostId))
            .Select(check => new UnifiNetworkResponse(
                check.HostId,
                hosts[check.HostId],
                Utc(check.CheckedAt),
                check.Site,
                check.Version,
                check.Problem,
                latest.TryGetValue(check.HostId, out var now)
                    ? new UnifiNow(Utc(now.Time), now.WanRxBps, now.WanTxBps, now.WiredClients, now.WirelessClients, now.GuestClients)
                    : null,
                devices[check.HostId]
                    .Select(device => new UnifiDeviceResponse(
                        device.Mac, device.Name, device.Model, device.Type, device.Online, device.UptimeSeconds, device.CpuPct,
                        device.MemPct, device.RxBps, device.TxBps, device.Clients, Utc(device.SeenAt), Utc(device.StateSince)))
                    .ToList()))
            .OrderBy(network => network.HostName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Internet traffic (<c>wanRx</c>, <c>wanTx</c>) and clients (<c>wired</c>, <c>wireless</c>, <c>guests</c>) over time.</summary>
    public async Task<MetricSeries> GetHistoryAsync(Guid hostId, SeriesRange range, TimeSpan collectionInterval, CancellationToken cancellationToken)
    {
        var (source, bucket) = SeriesResolution.ChooseRawOrHourly(range.To - range.From, range.Points, collectionInterval);
        var (table, timeColumn) = source == SeriesSource.Raw ? ("unifi_metrics", "time") : ("unifi_metrics_1h", "bucket");
        var sql = $"""
            SELECT time_bucket_gapfill(@bucket, {timeColumn}, @from, @to) AS bucket,
                   avg(wan_rx_bps)::float8 AS wan_rx, avg(wan_tx_bps)::float8 AS wan_tx,
                   avg(wired_clients)::float8 AS wired, avg(wireless_clients)::float8 AS wireless,
                   avg(guest_clients)::float8 AS guests
            FROM {table}
            WHERE host_id = @host_id AND {timeColumn} >= @from AND {timeColumn} < @to
            GROUP BY 1 ORDER BY 1
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            sql, new { host_id = hostId, bucket, from = range.From, to = range.To }, cancellationToken: cancellationToken))).ToList();

        return new MetricSeries(range.From, range.To, source.Label(), (int)bucket.TotalSeconds,
            rows.Select(row => Utc(row.Bucket).ToUnixTimeSeconds()).ToList(),
            new Dictionary<string, double?[]>
            {
                ["wanRx"] = [.. rows.Select(row => row.WanRx)],
                ["wanTx"] = [.. rows.Select(row => row.WanTx)],
                ["wired"] = [.. rows.Select(row => row.Wired)],
                ["wireless"] = [.. rows.Select(row => row.Wireless)],
                ["guests"] = [.. rows.Select(row => row.Guests)],
            });
    }

    /// <summary>
    /// What each client moved over the range, busiest first. Ranges over two days come from the hourly
    /// rollup, a whole hour at a time.
    /// </summary>
    public async Task<List<UnifiClientTotal>> GetClientTotalsAsync(Guid hostId, SeriesRange range, CancellationToken cancellationToken)
    {
        var raw = range.To - range.From <= TimeSpan.FromDays(2);
        var sql = raw
            ? """
              SELECT mac, last(name, time) AS name, last(ip, time) AS ip, last(wired, time) AS wired, last(network, time) AS network,
                     sum(downloaded_bytes)::bigint AS downloaded_bytes, sum(uploaded_bytes)::bigint AS uploaded_bytes,
                     max(time) AS last_seen
              FROM unifi_client_traffic
              WHERE host_id = @host_id AND time >= @from AND time < @to
              GROUP BY mac
              """
            : """
              SELECT mac, last(name, bucket) AS name, last(ip, bucket) AS ip, last(wired, bucket) AS wired,
                     last(network, bucket) AS network,
                     sum(downloaded_bytes)::bigint AS downloaded_bytes, sum(uploaded_bytes)::bigint AS uploaded_bytes,
                     max(bucket) + interval '1 hour' AS last_seen
              FROM unifi_client_traffic_1h
              WHERE host_id = @host_id AND bucket >= date_trunc('hour', @from) AND bucket < @to
              GROUP BY mac
              """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClientRow>(new CommandDefinition(
            sql, new { host_id = hostId, from = range.From, to = range.To }, cancellationToken: cancellationToken));

        return rows
            .Select(row => new UnifiClientTotal(
                row.Mac, row.Name, row.Ip, row.Wired, row.Network, row.DownloadedBytes, row.UploadedBytes,
                Utc(row.LastSeen) > range.To ? range.To : Utc(row.LastSeen)))
            .OrderByDescending(client => client.DownloadedBytes + client.UploadedBytes)
            .ThenBy(client => client.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset Utc(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private sealed class CheckRow
    {
        public Guid HostId { get; set; }
        public DateTime CheckedAt { get; set; }
        public string? Site { get; set; }
        public string? Version { get; set; }
        public string? Problem { get; set; }
    }

    private sealed class NowRow
    {
        public Guid HostId { get; set; }
        public DateTime Time { get; set; }
        public double? WanRxBps { get; set; }
        public double? WanTxBps { get; set; }
        public int? WiredClients { get; set; }
        public int? WirelessClients { get; set; }
        public int? GuestClients { get; set; }
    }

    private sealed class DeviceRow
    {
        public Guid HostId { get; set; }
        public string Mac { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Model { get; set; }
        public string? Type { get; set; }
        public bool Online { get; set; }
        public long? UptimeSeconds { get; set; }
        public double? CpuPct { get; set; }
        public double? MemPct { get; set; }
        public double? RxBps { get; set; }
        public double? TxBps { get; set; }
        public int? Clients { get; set; }
        public DateTime SeenAt { get; set; }
        public DateTime StateSince { get; set; }
    }

    private sealed class HistoryRow
    {
        public DateTime Bucket { get; set; }
        public double? WanRx { get; set; }
        public double? WanTx { get; set; }
        public double? Wired { get; set; }
        public double? Wireless { get; set; }
        public double? Guests { get; set; }
    }

    private sealed class ClientRow
    {
        public string Mac { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Ip { get; set; }
        public bool Wired { get; set; }
        public string? Network { get; set; }
        public long DownloadedBytes { get; set; }
        public long UploadedBytes { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
