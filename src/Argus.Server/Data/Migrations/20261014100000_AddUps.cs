using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// UPS readings: a hypertable with one row per UPS per sample, compressed after two days, and an
    /// hourly rollup for long ranges. Retention policies are applied at startup, like the other time
    /// series (see RetentionPolicies). Written with raw SQL, so not part of the EF model. Existing
    /// accounts also get the UPS rules new accounts start with (see DefaultAlertRules); they stay quiet
    /// on hosts without a UPS.
    /// </summary>
    public partial class AddUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE ups_metrics (
                    time                     timestamptz NOT NULL,
                    host_id                  uuid        NOT NULL,
                    ups                      text        NOT NULL,
                    model                    text,
                    status                   text        NOT NULL,
                    on_battery               boolean     NOT NULL,
                    low_battery              boolean     NOT NULL,
                    battery_charge_pct       real,
                    battery_runtime_seconds  real,
                    load_pct                 real,
                    input_voltage            real,
                    output_voltage           real,
                    battery_voltage          real,
                    PRIMARY KEY (host_id, ups, time)
                );
                SELECT create_hypertable('ups_metrics', by_range('time', INTERVAL '1 day'));

                ALTER TABLE ups_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id, ups',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('ups_metrics', compress_after => INTERVAL '2 days');
                """);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW ups_metrics_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    ups,
                    time_bucket(INTERVAL '1 hour', time)              AS bucket,
                    avg(battery_charge_pct)::double precision         AS battery_charge_pct,
                    min(battery_charge_pct)::double precision         AS battery_charge_pct_min,
                    avg(battery_runtime_seconds)::double precision    AS battery_runtime_seconds,
                    min(battery_runtime_seconds)::double precision    AS battery_runtime_seconds_min,
                    avg(load_pct)::double precision                   AS load_pct,
                    max(load_pct)::double precision                   AS load_pct_max,
                    max(on_battery::int)                              AS on_battery
                FROM ups_metrics
                GROUP BY host_id, ups, bucket
                WITH NO DATA;

                -- Like the other rollups, refreshed well inside the minimum raw retention (7 days).
                SELECT add_continuous_aggregate_policy('ups_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                """);

            migrationBuilder.Sql("""
                INSERT INTO alert_rules (
                    id, owner_id, name, metric, condition, operator, threshold, duration_seconds, severity, enabled, created_at, updated_at)
                SELECT gen_random_uuid(), u.id, r.name, r.metric, 'Threshold', r.operator, r.threshold, r.duration_seconds, r.severity,
                       true, now(), now()
                FROM users u
                CROSS JOIN (VALUES
                    ('UPS on battery',       'UpsOnBattery',     'Above', 0,  0,  'Warning'),
                    ('UPS battery low',      'UpsBatteryCharge', 'Below', 75, 60, 'Warning'),
                    ('UPS battery critical', 'UpsBatteryCharge', 'Below', 40, 60, 'Critical')
                ) AS r(name, metric, operator, threshold, duration_seconds, severity);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM alerts WHERE metric IN ('UpsOnBattery', 'UpsBatteryCharge', 'UpsBatteryRuntime', 'UpsLoad');
                DELETE FROM alert_rules WHERE metric IN ('UpsOnBattery', 'UpsBatteryCharge', 'UpsBatteryRuntime', 'UpsLoad');
                DROP MATERIALIZED VIEW IF EXISTS ups_metrics_1h;
                DROP TABLE IF EXISTS ups_metrics;
                """);
        }
    }
}
