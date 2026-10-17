using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertIgnores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ignored_at",
                table: "alerts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_ignores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_ignores", x => x.id);
                    table.ForeignKey(
                        name: "fk_alert_ignores_alert_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "alert_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alert_ignores_hosts_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alert_ignores_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_alert_ignores_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_ignores_created_by_id",
                table: "alert_ignores",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_ignores_host_id",
                table: "alert_ignores",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_ignores_owner_id",
                table: "alert_ignores",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_ignores_rule_id_host_id_resource_key",
                table: "alert_ignores",
                columns: new[] { "rule_id", "host_id", "resource_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_ignores");

            migrationBuilder.DropColumn(
                name: "ignored_at",
                table: "alerts");
        }
    }
}
