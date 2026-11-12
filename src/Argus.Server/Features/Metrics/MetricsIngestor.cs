using System.Text.Json;
using Argus.Contracts.Agent;
using Npgsql;
using NpgsqlTypes;

namespace Argus.Server.Features.Metrics;

/// <summary>
/// Writes agent samples into the TimescaleDB hypertables. All statements travel in one batch (one
/// round trip, one implicit transaction) and use <c>unnest</c> over column arrays, so a batch costs
/// a handful of statements no matter how many samples it holds. Re-sent samples hit the primary
/// keys and are skipped, which makes ingestion idempotent.
/// </summary>
public sealed class MetricsIngestor(NpgsqlDataSource dataSource)
{
    /// <summary>Stores the samples and marks the host as seen; returns how many samples were new.</summary>
    public async Task<int> IngestAsync(
        Guid hostId, IReadOnlyList<MetricSample> samples, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var batch = dataSource.CreateBatch();

        NpgsqlBatchCommand? hostMetrics = null;
        if (samples.Count > 0)
        {
            hostMetrics = HostMetrics(hostId, samples);
            batch.BatchCommands.Add(hostMetrics);
        }

        if (samples.Any(sample => sample.Filesystems.Count > 0))
        {
            batch.BatchCommands.Add(FilesystemMetrics(hostId, samples));
        }

        if (samples.Any(sample => sample.Interfaces.Count > 0))
        {
            batch.BatchCommands.Add(NetworkMetrics(hostId, samples));
        }

        if (samples.Any(sample => sample.Temperatures.Count > 0))
        {
            batch.BatchCommands.Add(TemperatureMetrics(hostId, samples));
        }

        if (samples.Any(sample => sample.Ups.Count > 0))
        {
            batch.BatchCommands.Add(UpsMetrics(hostId, samples));
        }

        if (samples.Any(sample => sample.ContainerUsage is { Count: > 0 }))
        {
            batch.BatchCommands.Add(ContainerMetrics(hostId, samples));
        }

        var unifi = samples.Where(sample => sample.Unifi is not null).ToList();
        if (unifi.Any(sample => sample.Unifi!.Problem is null))
        {
            batch.BatchCommands.Add(UnifiMetrics(hostId, unifi));
        }

        if (unifi.Any(sample => sample.Unifi!.Clients is { Count: > 0 }))
        {
            batch.BatchCommands.Add(UnifiClientTraffic(hostId, unifi));
        }

        if (unifi.MaxBy(sample => sample.Timestamp) is { } unifiCheck)
        {
            batch.BatchCommands.Add(UnifiCheck(hostId, unifiCheck));
        }

        if (unifi.Where(sample => sample.Unifi!.Devices is not null).MaxBy(sample => sample.Timestamp) is { } deviceReport)
        {
            batch.BatchCommands.Add(UnifiDevices(hostId, deviceReport));
        }

        if (samples.Where(sample => sample.TopProcesses is not null).MaxBy(sample => sample.Timestamp) is { } latest)
        {
            batch.BatchCommands.Add(ProcessSnapshot(hostId, latest));
        }

        if (samples.Where(sample => sample.FailedServices is not null).MaxBy(sample => sample.Timestamp) is { } serviceCheck)
        {
            batch.BatchCommands.Add(ServiceCheck(hostId, serviceCheck));
        }

        batch.BatchCommands.Add(new NpgsqlBatchCommand("UPDATE hosts SET last_seen_at = @now WHERE id = @host_id")
        {
            Parameters = { Param("now", now), Param("host_id", hostId) },
        });

        await batch.ExecuteNonQueryAsync(cancellationToken);
        return hostMetrics?.RecordsAffected ?? 0;
    }

    private static NpgsqlBatchCommand HostMetrics(Guid hostId, IReadOnlyList<MetricSample> samples) => new("""
        INSERT INTO host_metrics (
            host_id, time,
            cpu_usage_pct, cpu_user_pct, cpu_system_pct, cpu_iowait_pct, cpu_steal_pct,
            load_1, load_5, load_15,
            mem_total_bytes, mem_used_bytes, mem_available_bytes, mem_cached_bytes, swap_total_bytes, swap_used_bytes,
            disk_read_bps, disk_write_bps, disk_read_iops, disk_write_iops, disk_util_pct,
            net_rx_bps, net_tx_bps, process_count, uptime_seconds)
        SELECT @host_id, * FROM unnest(
            @time::timestamptz[],
            @cpu_usage::real[], @cpu_user::real[], @cpu_system::real[], @cpu_iowait::real[], @cpu_steal::real[],
            @load_1::real[], @load_5::real[], @load_15::real[],
            @mem_total::bigint[], @mem_used::bigint[], @mem_available::bigint[], @mem_cached::bigint[],
            @swap_total::bigint[], @swap_used::bigint[],
            @disk_read::float8[], @disk_write::float8[], @disk_read_iops::real[], @disk_write_iops::real[], @disk_util::real[],
            @net_rx::float8[], @net_tx::float8[], @process_count::integer[], @uptime::bigint[])
        ON CONFLICT DO NOTHING
        """)
    {
        Parameters =
        {
            Param("host_id", hostId),
            Param("time", samples.Select(s => s.Timestamp).ToArray()),
            Param("cpu_usage", samples.Select(s => (float)s.Cpu.UsagePercent).ToArray()),
            Param("cpu_user", samples.Select(s => Real(s.Cpu.UserPercent)).ToArray()),
            Param("cpu_system", samples.Select(s => Real(s.Cpu.SystemPercent)).ToArray()),
            Param("cpu_iowait", samples.Select(s => Real(s.Cpu.IowaitPercent)).ToArray()),
            Param("cpu_steal", samples.Select(s => Real(s.Cpu.StealPercent)).ToArray()),
            Param("load_1", samples.Select(s => Real(s.Load?.Load1)).ToArray()),
            Param("load_5", samples.Select(s => Real(s.Load?.Load5)).ToArray()),
            Param("load_15", samples.Select(s => Real(s.Load?.Load15)).ToArray()),
            Param("mem_total", samples.Select(s => s.Memory.TotalBytes).ToArray()),
            Param("mem_used", samples.Select(s => s.Memory.UsedBytes).ToArray()),
            Param("mem_available", samples.Select(s => s.Memory.AvailableBytes).ToArray()),
            Param("mem_cached", samples.Select(s => s.Memory.CachedBytes).ToArray()),
            Param("swap_total", samples.Select(s => s.Memory.SwapTotalBytes).ToArray()),
            Param("swap_used", samples.Select(s => s.Memory.SwapUsedBytes).ToArray()),
            Param("disk_read", samples.Select(s => s.DiskIo?.ReadBytesPerSec).ToArray()),
            Param("disk_write", samples.Select(s => s.DiskIo?.WriteBytesPerSec).ToArray()),
            Param("disk_read_iops", samples.Select(s => Real(s.DiskIo?.ReadOpsPerSec)).ToArray()),
            Param("disk_write_iops", samples.Select(s => Real(s.DiskIo?.WriteOpsPerSec)).ToArray()),
            Param("disk_util", samples.Select(s => Real(s.DiskIo?.UtilizationPercent)).ToArray()),
            Param("net_rx", samples.Select(s => s.Network?.RxBytesPerSec).ToArray()),
            Param("net_tx", samples.Select(s => s.Network?.TxBytesPerSec).ToArray()),
            Param("process_count", samples.Select(s => s.ProcessCount).ToArray()),
            Param("uptime", samples.Select(s => s.UptimeSeconds).ToArray()),
        },
    };

    private static NpgsqlBatchCommand FilesystemMetrics(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.SelectMany(s => s.Filesystems, (s, fs) => (s.Timestamp, Fs: fs)).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO filesystem_metrics (
                host_id, time, mount_point, device, fs_type,
                total_bytes, used_bytes, available_bytes, inodes_total, inodes_used)
            SELECT @host_id, * FROM unnest(
                @time::timestamptz[], @mount_point::text[], @device::text[], @fs_type::text[],
                @total::bigint[], @used::bigint[], @available::bigint[], @inodes_total::bigint[], @inodes_used::bigint[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("mount_point", rows.Select(r => r.Fs.MountPoint).ToArray()),
                Param("device", rows.Select(r => r.Fs.Device).ToArray()),
                Param("fs_type", rows.Select(r => r.Fs.FsType).ToArray()),
                Param("total", rows.Select(r => r.Fs.TotalBytes).ToArray()),
                Param("used", rows.Select(r => r.Fs.UsedBytes).ToArray()),
                Param("available", rows.Select(r => r.Fs.AvailableBytes).ToArray()),
                Param("inodes_total", rows.Select(r => r.Fs.InodesTotal).ToArray()),
                Param("inodes_used", rows.Select(r => r.Fs.InodesUsed).ToArray()),
            },
        };
    }

    private static NpgsqlBatchCommand NetworkMetrics(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.SelectMany(s => s.Interfaces, (s, nic) => (s.Timestamp, Nic: nic)).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO network_metrics (
                host_id, time, interface, rx_bps, tx_bps, rx_pps, tx_pps, rx_errors_ps, tx_errors_ps)
            SELECT @host_id, * FROM unnest(
                @time::timestamptz[], @interface::text[], @rx::float8[], @tx::float8[],
                @rx_pps::real[], @tx_pps::real[], @rx_errors::real[], @tx_errors::real[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("interface", rows.Select(r => r.Nic.Name).ToArray()),
                Param("rx", rows.Select(r => r.Nic.RxBytesPerSec).ToArray()),
                Param("tx", rows.Select(r => r.Nic.TxBytesPerSec).ToArray()),
                Param("rx_pps", rows.Select(r => (float)r.Nic.RxPacketsPerSec).ToArray()),
                Param("tx_pps", rows.Select(r => (float)r.Nic.TxPacketsPerSec).ToArray()),
                Param("rx_errors", rows.Select(r => (float)r.Nic.RxErrorsPerSec).ToArray()),
                Param("tx_errors", rows.Select(r => (float)r.Nic.TxErrorsPerSec).ToArray()),
            },
        };
    }

    private static NpgsqlBatchCommand TemperatureMetrics(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.SelectMany(s => s.Temperatures, (s, reading) => (s.Timestamp, Reading: reading)).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO temperature_metrics (host_id, time, device, sensor, celsius)
            SELECT @host_id, * FROM unnest(@time::timestamptz[], @device::text[], @sensor::text[], @celsius::real[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("device", rows.Select(r => r.Reading.Device).ToArray()),
                Param("sensor", rows.Select(r => r.Reading.Sensor).ToArray()),
                Param("celsius", rows.Select(r => (float)r.Reading.Celsius).ToArray()),
            },
        };
    }

    /// <summary>One row per UPS per sample. The status flags are split out too, so alerts and charts need no parsing.</summary>
    private static NpgsqlBatchCommand UpsMetrics(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.SelectMany(s => s.Ups, (s, ups) => (s.Timestamp, Ups: ups)).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO ups_metrics (
                host_id, time, ups, model, status, on_battery, low_battery,
                battery_charge_pct, battery_runtime_seconds, load_pct, input_voltage, output_voltage, battery_voltage)
            SELECT @host_id, * FROM unnest(
                @time::timestamptz[], @ups::text[], @model::text[], @status::text[], @on_battery::boolean[], @low_battery::boolean[],
                @charge::real[], @runtime::real[], @load::real[], @input::real[], @output::real[], @battery::real[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("ups", rows.Select(r => r.Ups.Name).ToArray()),
                Param("model", rows.Select(r => r.Ups.Model).ToArray()),
                Param("status", rows.Select(r => r.Ups.Status).ToArray()),
                Param("on_battery", rows.Select(r => UpsStatus.OnBattery(r.Ups.Status)).ToArray()),
                Param("low_battery", rows.Select(r => UpsStatus.LowBattery(r.Ups.Status)).ToArray()),
                Param("charge", rows.Select(r => Real(r.Ups.BatteryChargePercent)).ToArray()),
                Param("runtime", rows.Select(r => Real(r.Ups.BatteryRuntimeSeconds)).ToArray()),
                Param("load", rows.Select(r => Real(r.Ups.LoadPercent)).ToArray()),
                Param("input", rows.Select(r => Real(r.Ups.InputVoltage)).ToArray()),
                Param("output", rows.Select(r => Real(r.Ups.OutputVoltage)).ToArray()),
                Param("battery", rows.Select(r => Real(r.Ups.BatteryVoltage)).ToArray()),
            },
        };
    }

    private static NpgsqlBatchCommand ContainerMetrics(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.SelectMany(s => s.ContainerUsage ?? [], (s, usage) => (s.Timestamp, Usage: usage)).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO container_metrics (host_id, time, container, cpu_pct, mem_used_bytes, mem_limit_bytes, net_rx_bps, net_tx_bps)
            SELECT @host_id, * FROM unnest(
                @time::timestamptz[], @container::text[], @cpu::real[], @memory::bigint[], @memory_limit::bigint[],
                @net_rx::float8[], @net_tx::float8[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("container", rows.Select(r => r.Usage.Name).ToArray()),
                Param("cpu", rows.Select(r => (float)r.Usage.CpuPercent).ToArray()),
                Param("memory", rows.Select(r => r.Usage.MemoryBytes).ToArray()),
                Param("memory_limit", rows.Select(r => r.Usage.MemoryLimitBytes).ToArray()),
                Param("net_rx", rows.Select(r => r.Usage.NetRxBytesPerSec).ToArray()),
                Param("net_tx", rows.Select(r => r.Usage.NetTxBytesPerSec).ToArray()),
            },
        };
    }

    /// <summary>Internet traffic and client counts, one row per sample the console answered.</summary>
    private static NpgsqlBatchCommand UnifiMetrics(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.Where(s => s.Unifi!.Problem is null).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO unifi_metrics (host_id, time, wan_rx_bps, wan_tx_bps, wired_clients, wireless_clients, guest_clients)
            SELECT @host_id, * FROM unnest(
                @time::timestamptz[], @wan_rx::float8[], @wan_tx::float8[], @wired::integer[], @wireless::integer[], @guests::integer[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("wan_rx", rows.Select(r => r.Unifi!.WanRxBytesPerSec).ToArray()),
                Param("wan_tx", rows.Select(r => r.Unifi!.WanTxBytesPerSec).ToArray()),
                Param("wired", rows.Select(r => r.Unifi!.WiredClients).ToArray()),
                Param("wireless", rows.Select(r => r.Unifi!.WirelessClients).ToArray()),
                Param("guests", rows.Select(r => r.Unifi!.GuestClients).ToArray()),
            },
        };
    }

    /// <summary>What each client moved between the agent's client reports.</summary>
    private static NpgsqlBatchCommand UnifiClientTraffic(Guid hostId, IReadOnlyList<MetricSample> samples)
    {
        var rows = samples.SelectMany(s => s.Unifi!.Clients ?? [], (s, client) => (s.Timestamp, Client: client)).ToList();
        return new NpgsqlBatchCommand("""
            INSERT INTO unifi_client_traffic (host_id, time, mac, name, ip, wired, network, downloaded_bytes, uploaded_bytes)
            SELECT @host_id, * FROM unnest(
                @time::timestamptz[], @mac::text[], @name::text[], @ip::text[], @wired::boolean[], @network::text[],
                @downloaded::bigint[], @uploaded::bigint[])
            ON CONFLICT DO NOTHING
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("time", rows.Select(r => r.Timestamp).ToArray()),
                Param("mac", rows.Select(r => r.Client.Mac).ToArray()),
                Param("name", rows.Select(r => r.Client.Name).ToArray()),
                Param("ip", rows.Select(r => r.Client.Ip).ToArray()),
                Param("wired", rows.Select(r => r.Client.Wired).ToArray()),
                Param("network", rows.Select(r => r.Client.Network).ToArray()),
                Param("downloaded", rows.Select(r => r.Client.DownloadedBytes).ToArray()),
                Param("uploaded", rows.Select(r => r.Client.UploadedBytes).ToArray()),
            },
        };
    }

    /// <summary>The newest check of the console: when, which site and version, and what went wrong if anything.</summary>
    private static NpgsqlBatchCommand UnifiCheck(Guid hostId, MetricSample sample) => new("""
        INSERT INTO unifi_checks (host_id, checked_at, site, version, problem)
        VALUES (@host_id, @checked_at, @site, @version, @problem)
        ON CONFLICT (host_id) DO UPDATE
            SET checked_at = excluded.checked_at,
                site = coalesce(excluded.site, unifi_checks.site),
                version = coalesce(excluded.version, unifi_checks.version),
                problem = excluded.problem
            WHERE unifi_checks.checked_at < excluded.checked_at
        """)
    {
        Parameters =
        {
            Param("host_id", hostId),
            Param("checked_at", sample.Timestamp),
            Param("site", sample.Unifi!.Site),
            Param("version", sample.Unifi.Version),
            Param("problem", sample.Unifi.Problem),
        },
    };

    /// <summary>
    /// Replaces the site's devices with the newest report. A device keeps the time it went online or offline
    /// until that changes; devices no longer listed are removed. An older report than the stored one changes nothing.
    /// </summary>
    private static NpgsqlBatchCommand UnifiDevices(Guid hostId, MetricSample sample)
    {
        var devices = sample.Unifi!.Devices!;
        return new NpgsqlBatchCommand("""
            WITH newest AS (
                SELECT @seen_at::timestamptz AS seen_at
                WHERE NOT EXISTS (SELECT 1 FROM unifi_devices WHERE host_id = @host_id AND seen_at >= @seen_at)),
            gone AS (
                DELETE FROM unifi_devices
                WHERE host_id = @host_id AND EXISTS (SELECT 1 FROM newest) AND NOT (mac = ANY(@mac)))
            INSERT INTO unifi_devices (
                host_id, mac, name, model, type, online, uptime_seconds, cpu_pct, mem_pct, rx_bps, tx_bps, clients, seen_at, state_since)
            SELECT @host_id, d.mac, d.name, d.model, d.type, d.online, d.uptime, d.cpu, d.mem, d.rx, d.tx, d.clients, n.seen_at, n.seen_at
            FROM newest n
            CROSS JOIN unnest(
                @mac::text[], @name::text[], @model::text[], @type::text[], @online::boolean[], @uptime::bigint[],
                @cpu::real[], @mem::real[], @rx::float8[], @tx::float8[], @clients::integer[])
                AS d(mac, name, model, type, online, uptime, cpu, mem, rx, tx, clients)
            ON CONFLICT (host_id, mac) DO UPDATE
                SET name = excluded.name, model = excluded.model, type = excluded.type, online = excluded.online,
                    uptime_seconds = excluded.uptime_seconds, cpu_pct = excluded.cpu_pct, mem_pct = excluded.mem_pct,
                    rx_bps = excluded.rx_bps, tx_bps = excluded.tx_bps, clients = excluded.clients, seen_at = excluded.seen_at,
                    state_since = CASE WHEN unifi_devices.online = excluded.online THEN unifi_devices.state_since ELSE excluded.seen_at END
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("seen_at", sample.Timestamp),
                Param("mac", devices.Select(d => d.Mac).ToArray()),
                Param("name", devices.Select(d => d.Name).ToArray()),
                Param("model", devices.Select(d => d.Model).ToArray()),
                Param("type", devices.Select(d => d.Type).ToArray()),
                Param("online", devices.Select(d => d.Online).ToArray()),
                Param("uptime", devices.Select(d => d.UptimeSeconds).ToArray()),
                Param("cpu", devices.Select(d => Real(d.CpuPercent)).ToArray()),
                Param("mem", devices.Select(d => Real(d.MemoryPercent)).ToArray()),
                Param("rx", devices.Select(d => d.RxBytesPerSec).ToArray()),
                Param("tx", devices.Select(d => d.TxBytesPerSec).ToArray()),
                Param("clients", devices.Select(d => d.Clients).ToArray()),
            },
        };
    }

    /// <summary>Keeps only the newest top-process list per host.</summary>
    private static NpgsqlBatchCommand ProcessSnapshot(Guid hostId, MetricSample sample) => new("""
        INSERT INTO host_processes (host_id, captured_at, processes)
        VALUES (@host_id, @captured_at, @processes)
        ON CONFLICT (host_id) DO UPDATE
            SET captured_at = excluded.captured_at, processes = excluded.processes
            WHERE host_processes.captured_at < excluded.captured_at
        """)
    {
        Parameters =
        {
            Param("host_id", hostId),
            Param("captured_at", sample.Timestamp),
            new NpgsqlParameter("processes", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(sample.TopProcesses, JsonSerializerOptions.Web),
            },
        },
    };

    /// <summary>
    /// Applies the newest service check. A failure keeps the time it was first seen for as long as it
    /// lasts, recovered services are removed, and a check older than the stored one changes nothing.
    /// </summary>
    private static NpgsqlBatchCommand ServiceCheck(Guid hostId, MetricSample sample)
    {
        var failures = sample.FailedServices!;
        return new NpgsqlBatchCommand("""
            WITH checked AS (
                INSERT INTO host_service_checks (host_id, checked_at) VALUES (@host_id, @checked_at)
                ON CONFLICT (host_id) DO UPDATE SET checked_at = excluded.checked_at
                    WHERE host_service_checks.checked_at < excluded.checked_at
                RETURNING checked_at),
            recovered AS (
                DELETE FROM host_service_failures
                WHERE host_id = @host_id AND EXISTS (SELECT 1 FROM checked) AND NOT (service = ANY(@services)))
            INSERT INTO host_service_failures (host_id, service, description, state, since, last_seen)
            SELECT @host_id, f.service, f.description, f.state, c.checked_at, c.checked_at
            FROM checked c
            CROSS JOIN unnest(@services::text[], @descriptions::text[], @states::text[]) AS f(service, description, state)
            ON CONFLICT (host_id, service) DO UPDATE
                SET description = excluded.description, state = excluded.state, last_seen = excluded.last_seen
            """)
        {
            Parameters =
            {
                Param("host_id", hostId),
                Param("checked_at", sample.Timestamp),
                Param("services", failures.Select(failure => failure.Name).ToArray()),
                Param("descriptions", failures.Select(failure => failure.Description).ToArray()),
                Param("states", failures.Select(failure => failure.State).ToArray()),
            },
        };
    }

    private static NpgsqlParameter<T> Param<T>(string name, T value) => new(name, value);

    private static float? Real(double? value) => value is { } v ? (float)v : null;
}
