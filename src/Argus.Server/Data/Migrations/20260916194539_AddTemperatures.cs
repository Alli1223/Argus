using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Temperature readings: a hypertable with one row per sensor per sample, compressed after two days,
    /// and an hourly rollup for long ranges. Retention policies are applied at startup, like the other
    /// time series (see RetentionPolicies). Written with raw SQL, so not part of the EF model.
    /// </summary>
    public partial class AddTemperatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE temperature_metrics (
                    time     timestamptz NOT NULL,
                    host_id  uuid        NOT NULL,
                    device   text        NOT NULL,
                    sensor   text        NOT NULL,
                    celsius  real        NOT NULL,
                    PRIMARY KEY (host_id, device, sensor, time)
                );
                SELECT create_hypertable('temperature_metrics', by_range('time', INTERVAL '1 day'));

                ALTER TABLE temperature_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id, device, sensor',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('temperature_metrics', compress_after => INTERVAL '2 days');
                """);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW temperature_metrics_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    device,
                    sensor,
                    time_bucket(INTERVAL '1 hour', time)  AS bucket,
                    avg(celsius)::double precision        AS celsius,
                    max(celsius)::double precision        AS celsius_max
                FROM temperature_metrics
                GROUP BY host_id, device, sensor, bucket
                WITH NO DATA;

                -- Like the other rollups, refreshed well inside the minimum raw retention (7 days).
                SELECT add_continuous_aggregate_policy('temperature_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP MATERIALIZED VIEW IF EXISTS temperature_metrics_1h;
                DROP TABLE IF EXISTS temperature_metrics;
                """);
        }
    }
}
