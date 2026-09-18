using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Settings administrators change in the web app rather than in the Compose file, one row per group
    /// of them (see EmailSettingsStore). Written with raw SQL, so not part of the EF model.
    /// </summary>
    public partial class AddServerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE server_settings (
                    key         text        PRIMARY KEY,
                    value       jsonb       NOT NULL,
                    updated_at  timestamptz NOT NULL,
                    updated_by  text
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS server_settings;");
        }
    }
}
