using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Docker containers reported by agents: each host's newest report (host_container_checks and
    /// host_containers), what changed between reports (container_events), and what running containers used
    /// (container_metrics, compressed after two days, with an hourly rollup). Retention policies are applied
    /// at startup (see RetentionPolicies). Written with raw SQL, so not part of the EF model.
    /// </summary>
    public partial class AddContainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE host_container_checks (
                    host_id          uuid        PRIMARY KEY REFERENCES hosts (id) ON DELETE CASCADE,
                    checked_at       timestamptz NOT NULL,
                    engine_version   text,
                    problem          text,
                    actions_enabled  boolean     NOT NULL
                );

                CREATE TABLE host_containers (
                    host_id          uuid        NOT NULL REFERENCES hosts (id) ON DELETE CASCADE,
                    name             text        NOT NULL,
                    container_id     text        NOT NULL,
                    image            text        NOT NULL,
                    state            text        NOT NULL,
                    health           text,
                    restart_count    integer     NOT NULL,
                    exit_code        integer,
                    oom_killed       boolean     NOT NULL,
                    created_at       timestamptz NOT NULL,
                    started_at       timestamptz,
                    finished_at      timestamptz,
                    restart_policy   text,
                    compose_project  text,
                    compose_service  text,
                    ports            text[]      NOT NULL,
                    state_since      timestamptz NOT NULL,
                    PRIMARY KEY (host_id, name)
                );

                CREATE TABLE container_events (
                    time       timestamptz NOT NULL,
                    host_id    uuid        NOT NULL,
                    container  text        NOT NULL,
                    kind       text        NOT NULL,
                    detail     text,
                    count      integer     NOT NULL
                );
                SELECT create_hypertable('container_events', by_range('time', INTERVAL '7 days'));
                CREATE INDEX container_events_container_time ON container_events (host_id, container, time DESC);

                CREATE TABLE container_metrics (
                    time             timestamptz      NOT NULL,
                    host_id          uuid             NOT NULL,
                    container        text             NOT NULL,
                    cpu_pct          real             NOT NULL,
                    mem_used_bytes   bigint           NOT NULL,
                    mem_limit_bytes  bigint,
                    net_rx_bps       double precision,
                    net_tx_bps       double precision,
                    PRIMARY KEY (host_id, container, time)
                );
                SELECT create_hypertable('container_metrics', by_range('time', INTERVAL '1 day'));

                ALTER TABLE container_metrics SET (
                    timescaledb.compress,
                    timescaledb.compress_segmentby = 'host_id, container',
                    timescaledb.compress_orderby = 'time DESC');
                SELECT add_compression_policy('container_metrics', compress_after => INTERVAL '2 days');
                """);

            migrationBuilder.Sql("""
                CREATE MATERIALIZED VIEW container_metrics_1h
                WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
                SELECT
                    host_id,
                    container,
                    time_bucket(INTERVAL '1 hour', time)    AS bucket,
                    avg(cpu_pct)::double precision          AS cpu_pct,
                    max(cpu_pct)::double precision          AS cpu_pct_max,
                    avg(mem_used_bytes)::double precision   AS mem_used_bytes,
                    max(mem_used_bytes)                     AS mem_used_bytes_max,
                    max(mem_limit_bytes)                    AS mem_limit_bytes,
                    avg(net_rx_bps)                         AS net_rx_bps,
                    avg(net_tx_bps)                         AS net_tx_bps
                FROM container_metrics
                GROUP BY host_id, container, bucket
                WITH NO DATA;

                -- Like the other rollups, refreshed well inside the minimum raw retention (7 days).
                SELECT add_continuous_aggregate_policy('container_metrics_1h',
                    start_offset => INTERVAL '3 days', end_offset => INTERVAL '1 hour', schedule_interval => INTERVAL '30 minutes');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP MATERIALIZED VIEW IF EXISTS container_metrics_1h;
                DROP TABLE IF EXISTS container_metrics;
                DROP TABLE IF EXISTS container_events;
                DROP TABLE IF EXISTS host_containers;
                DROP TABLE IF EXISTS host_container_checks;
                """);
        }
    }
}
