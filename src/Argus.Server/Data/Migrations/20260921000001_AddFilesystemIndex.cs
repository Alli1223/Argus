using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Adds an index on (host_id, time DESC) to filesystem_metrics so the hosts list can find each
    /// host's latest disk snapshot without a full-table scan.
    /// </summary>
    public partial class AddFilesystemIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_filesystem_metrics_host_id_time ON filesystem_metrics (host_id, time DESC);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_filesystem_metrics_host_id_time;",
                suppressTransaction: true);
        }
    }
}
