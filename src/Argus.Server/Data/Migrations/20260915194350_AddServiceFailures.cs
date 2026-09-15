using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Service checks reported by agents: when each host last checked, and which services were failing
    /// then, each with the time it was first seen failing. Written with raw SQL, like the other agent
    /// data, so not part of the EF model.
    /// </summary>
    public partial class AddServiceFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE host_service_checks (
                    host_id     uuid        PRIMARY KEY REFERENCES hosts (id) ON DELETE CASCADE,
                    checked_at  timestamptz NOT NULL
                );

                CREATE TABLE host_service_failures (
                    host_id      uuid        NOT NULL REFERENCES hosts (id) ON DELETE CASCADE,
                    service      text        NOT NULL,
                    description  text,
                    state        text        NOT NULL,
                    since        timestamptz NOT NULL,
                    last_seen    timestamptz NOT NULL,
                    PRIMARY KEY (host_id, service)
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS host_service_failures;
                DROP TABLE IF EXISTS host_service_checks;
                """);
        }
    }
}
