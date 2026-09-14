using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Time-series storage: TimescaleDB hypertables for samples (compressed after two days), the
    /// latest top-process snapshot per host, and continuous aggregates used for long time ranges.
    /// Retention policies are applied at startup from configuration (see RetentionPolicies).
    /// These tables are written with raw SQL, so they are not part of the EF model.
    /// </summary>
    public partial class AddTimeSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE host_metrics (
                    time                 timestamptz      NOT NULL,
                    host_id              uuid             NOT NULL,
                    cpu_usage_pct        real             NOT NULL,
                    cpu_user_pct         real,
                    cpu_system_pct       real,
                    cpu_iowait_pct       real,
                    cpu_steal_pct        real,
                    load_1               real,
                    load_5               real,
                    load_15              real,
                    mem_total_bytes      bigint           NOT NULL,
                    mem_used_bytes       bigint           NOT NULL,
                    mem_available_bytes  bigint           NOT NULL,
                    mem_cached_bytes     bigint,
                    swap_total_bytes     bigint           NOT NULL,
                    swap_used_bytes      bigint           NOT NULL,
                    disk_read_bps        double precision,
                    disk_write_bps       double precision,
                    disk_read_iops       real,
                    disk_write_iops      real,
                    disk_util_pct        real,
                    net_rx_bps           double precision,
                    net_tx_bps           double precision,
                    process_count        integer          NOT NULL,
                    uptime_seconds       bigint           NOT NULL,
                    PRIMARY KEY (host_id, time)
                );
                SELECT create_hypertable('host_metrics', by_range('time', INTERVAL '1 day'));

                CREATE TABLE filesystem_metrics (
                    time             timestamptz NOT NULL,
                    host_id          uuid        NOT NULL,
                    mount_point      text        NOT NULL,
                    device           text,
                    fs_type          text,
                    total_bytes      bigint      NOT NULL,
                    used_bytes       bigint      NOT NULL,
                    available_bytes  bigint      NOT NULL,
                    inodes_total     bigint,
                    inodes_used      bigint,
                    PRIMARY KEY (host_id, mount_point, time)
                );
                SELECT create_hypertable('filesystem_metrics', by_range('time', INTERVAL '1 day'));

                CREATE TABLE network_metrics (
                    time          timestamptz      NOT NULL,
                    host_id       uuid             NOT NULL,
                    interface     text             NOT NULL,
                    rx_bps        double precision NOT NULL,
                    tx_bps        double precision NOT NULL,
                    rx_pps        real             NOT NULL,
                    tx_pps        real             NOT NULL,
                    rx_errors_ps  real             NOT NULL,
                    tx_errors_ps  real             NOT NULL,
                    PRIMARY KEY (host_id, interface, time)
                );
                SELECT create_hypertable('network_metrics', by_range('time', INTERVAL '1 day'));

                CREATE TABLE host_processes (
                    host_id      uuid        PRIMARY KEY REFERENCES hosts (id) ON DELETE CASCADE,
                    captured_at  timestamptz NOT NULL,
                    processes    jsonb       NOT NULL
                );
                """);

            // Columnar compression: segmenting by host keeps per-host queries fast on compressed chunks.
            migrationBuilder.Sql("""
                ALTER TABLE host_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('host_metrics', compress_after => INTERVAL '2 days');

                ALTER TABLE filesystem_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id, mount_point',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('filesystem_metrics', compress_after => INTERVAL '2 days');

                ALTER TABLE network_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id, interface',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('network_metrics', compress_after => INTERVAL '2 days');
                """);

            // Rollups for long time ranges. Real-time aggregation (materialized_only = false) fills in
            // the newest, not yet materialised buckets from raw data so charts stay current.
            foreach (var (name, bucket) in new[] { ("host_metrics_5m", "5 minutes"), ("host_metrics_1h", "1 hour") })
            {
                migrationBuilder.Sql($"""
                    CREATE MATERIALIZED VIEW {name}
                    WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                    SELECT
                        host_id,
                        time_bucket(INTERVAL '{bucket}', time)       AS bucket,
                        avg(cpu_usage_pct)::double precision         AS cpu_usage_pct,
                        max(cpu_usage_pct)::double precision         AS cpu_usage_pct_max,
                        avg(cpu_iowait_pct)::double precision        AS cpu_iowait_pct,
                        avg(load_1)::double precision                AS load_1,
                        avg(load_5)::double precision                AS load_5,
                        avg(load_15)::double precision               AS load_15,
                        max(mem_total_bytes)                         AS mem_total_bytes,
                        avg(mem_used_bytes)::double precision        AS mem_used_bytes,
                        max(mem_used_bytes)                          AS mem_used_bytes_max,
                        max(swap_total_bytes)                        AS swap_total_bytes,
                        avg(swap_used_bytes)::double precision       AS swap_used_bytes,
                        avg(disk_read_bps)                           AS disk_read_bps,
                        max(disk_read_bps)                           AS disk_read_bps_max,
                        avg(disk_write_bps)                          AS disk_write_bps,
                        max(disk_write_bps)                          AS disk_write_bps_max,
                        avg(disk_util_pct)::double precision         AS disk_util_pct,
                        max(disk_util_pct)::double precision         AS disk_util_pct_max,
                        avg(net_rx_bps)                              AS net_rx_bps,
                        max(net_rx_bps)                              AS net_rx_bps_max,
                        avg(net_tx_bps)                              AS net_tx_bps,
                        max(net_tx_bps)                              AS net_tx_bps_max,
                        avg(process_count)::double precision         AS process_count,
                        count(*)                                     AS samples
                    FROM host_metrics
                    GROUP BY host_id, bucket
                    WITH NO DATA;
                    """);
            }

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW filesystem_metrics_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    mount_point,
                    time_bucket(INTERVAL '1 hour', time)    AS bucket,
                    max(total_bytes)                        AS total_bytes,
                    avg(used_bytes)::double precision       AS used_bytes,
                    max(used_bytes)                         AS used_bytes_max,
                    avg(available_bytes)::double precision  AS available_bytes,
                    min(available_bytes)                    AS available_bytes_min
                FROM filesystem_metrics
                GROUP BY host_id, mount_point, bucket
                WITH NO DATA;

                CREATE MATERIALIZED VIEW network_metrics_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    interface,
                    time_bucket(INTERVAL '1 hour', time)    AS bucket,
                    avg(rx_bps)                             AS rx_bps,
                    max(rx_bps)                             AS rx_bps_max,
                    avg(tx_bps)                             AS tx_bps,
                    max(tx_bps)                             AS tx_bps_max,
                    avg(rx_errors_ps)::double precision     AS rx_errors_ps,
                    avg(tx_errors_ps)::double precision     AS tx_errors_ps
                FROM network_metrics
                GROUP BY host_id, interface, bucket
                WITH NO DATA;

                -- Refresh windows stay well inside the minimum raw retention (7 days), so a refresh
                -- never recomputes a bucket whose raw data has already been dropped.
                SELECT add_continuous_aggregate_policy('host_metrics_5m',
                    start_offset => INTERVAL '1 day', end_offset => INTERVAL '5 minutes', schedule_interval => INTERVAL '5 minutes');
                SELECT add_continuous_aggregate_policy('host_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                SELECT add_continuous_aggregate_policy('filesystem_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                SELECT add_continuous_aggregate_policy('network_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP MATERIALIZED VIEW IF EXISTS network_metrics_1h;
                DROP MATERIALIZED VIEW IF EXISTS filesystem_metrics_1h;
                DROP MATERIALIZED VIEW IF EXISTS host_metrics_1h;
                DROP MATERIALIZED VIEW IF EXISTS host_metrics_5m;
                DROP TABLE IF EXISTS host_processes;
                DROP TABLE IF EXISTS network_metrics;
                DROP TABLE IF EXISTS filesystem_metrics;
                DROP TABLE IF EXISTS host_metrics;
                """);
        }
    }
}
