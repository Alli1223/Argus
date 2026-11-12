using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// UniFi networks, as agents read them from UniFi consoles: internet traffic and client counts, and each
    /// client's traffic, in hypertables with hourly rollups; the newest check of each console and the newest
    /// state of each device in plain tables. Written with raw SQL, so not part of the EF model. Retention
    /// policies are applied at startup (see RetentionPolicies). Existing accounts also get the "UniFi device
    /// offline" rule new accounts start with (see DefaultAlertRules).
    /// </summary>
    public partial class AddUnifi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE unifi_metrics (
                    time              timestamptz NOT NULL,
                    host_id           uuid        NOT NULL,
                    wan_rx_bps        double precision,
                    wan_tx_bps        double precision,
                    wired_clients     integer,
                    wireless_clients  integer,
                    guest_clients     integer,
                    PRIMARY KEY (host_id, time)
                );
                SELECT create_hypertable('unifi_metrics', by_range('time', INTERVAL '1 day'));
                ALTER TABLE unifi_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('unifi_metrics', compress_after => INTERVAL '2 days');

                CREATE TABLE unifi_client_traffic (
                    time              timestamptz NOT NULL,
                    host_id           uuid        NOT NULL,
                    mac               text        NOT NULL,
                    name              text        NOT NULL,
                    ip                text,
                    wired             boolean     NOT NULL,
                    network           text,
                    downloaded_bytes  bigint      NOT NULL,
                    uploaded_bytes    bigint      NOT NULL,
                    PRIMARY KEY (host_id, mac, time)
                );
                SELECT create_hypertable('unifi_client_traffic', by_range('time', INTERVAL '1 day'));
                ALTER TABLE unifi_client_traffic SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id, mac',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('unifi_client_traffic', compress_after => INTERVAL '2 days');

                CREATE TABLE unifi_checks (
                    host_id     uuid        PRIMARY KEY REFERENCES hosts (id) ON DELETE CASCADE,
                    checked_at  timestamptz NOT NULL,
                    site        text,
                    version     text,
                    problem     text
                );

                CREATE TABLE unifi_devices (
                    host_id         uuid        NOT NULL REFERENCES hosts (id) ON DELETE CASCADE,
                    mac             text        NOT NULL,
                    name            text        NOT NULL,
                    model           text,
                    type            text,
                    online          boolean     NOT NULL,
                    uptime_seconds  bigint,
                    cpu_pct         real,
                    mem_pct         real,
                    rx_bps          double precision,
                    tx_bps          double precision,
                    clients         integer,
                    seen_at         timestamptz NOT NULL,
                    -- When the device last went online or offline.
                    state_since     timestamptz NOT NULL,
                    PRIMARY KEY (host_id, mac)
                );
                """);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW unifi_metrics_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    time_bucket(INTERVAL '1 hour', time)   AS bucket,
                    avg(wan_rx_bps)                        AS wan_rx_bps,
                    max(wan_rx_bps)                        AS wan_rx_bps_max,
                    avg(wan_tx_bps)                        AS wan_tx_bps,
                    max(wan_tx_bps)                        AS wan_tx_bps_max,
                    avg(wired_clients)::double precision   AS wired_clients,
                    avg(wireless_clients)::double precision AS wireless_clients,
                    avg(guest_clients)::double precision   AS guest_clients,
                    count(*)                               AS samples
                FROM unifi_metrics
                GROUP BY host_id, bucket
                WITH NO DATA;

                CREATE MATERIALIZED VIEW unifi_client_traffic_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    mac,
                    time_bucket(INTERVAL '1 hour', time)   AS bucket,
                    last(name, time)                       AS name,
                    last(ip, time)                         AS ip,
                    last(wired, time)                      AS wired,
                    last(network, time)                    AS network,
                    sum(downloaded_bytes)                  AS downloaded_bytes,
                    sum(uploaded_bytes)                    AS uploaded_bytes
                FROM unifi_client_traffic
                GROUP BY host_id, mac, bucket
                WITH NO DATA;

                -- Like the other rollups, refreshed well inside the minimum raw retention (7 days).
                SELECT add_continuous_aggregate_policy('unifi_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                SELECT add_continuous_aggregate_policy('unifi_client_traffic_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                """);

            migrationBuilder.Sql("""
                INSERT INTO alert_rules (
                    id, owner_id, name, metric, condition, operator, threshold, duration_seconds, severity, enabled, created_at, updated_at)
                SELECT gen_random_uuid(), u.id, 'UniFi device offline', 'UnifiDeviceOffline', 'Threshold', 'Above', 0, 120, 'Warning',
                       true, now(), now()
                FROM users u;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM alerts WHERE metric = 'UnifiDeviceOffline';
                DELETE FROM alert_rules WHERE metric = 'UnifiDeviceOffline';
                DROP MATERIALIZED VIEW IF EXISTS unifi_client_traffic_1h;
                DROP MATERIALIZED VIEW IF EXISTS unifi_metrics_1h;
                DROP TABLE IF EXISTS unifi_devices;
                DROP TABLE IF EXISTS unifi_checks;
                DROP TABLE IF EXISTS unifi_client_traffic;
                DROP TABLE IF EXISTS unifi_metrics;
                """);
        }
    }
}
