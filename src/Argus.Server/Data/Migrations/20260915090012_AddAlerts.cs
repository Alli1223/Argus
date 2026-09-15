using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metric = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(16)", maxLength: 16, nullable: false),
                    threshold = table.Column<double>(type: "double precision", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    resource_filter = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_alert_rules_hosts_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alert_rules_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    metric = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    @operator = table.Column<string>(name: "operator", type: "character varying(16)", maxLength: 16, nullable: false),
                    threshold = table.Column<double>(type: "double precision", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: true),
                    fired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerts_alert_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "alert_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_alerts_hosts_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alerts_users_acknowledged_by_id",
                        column: x => x.acknowledged_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_alerts_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_rules_host_id",
                table: "alert_rules",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_alert_rules_owner_id",
                table: "alert_rules",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_acknowledged_by_id",
                table: "alerts",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_host_id",
                table: "alerts",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_owner_id_status_fired_at",
                table: "alerts",
                columns: new[] { "owner_id", "status", "fired_at" });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_rule_id_host_id_resource_key",
                table: "alerts",
                columns: new[] { "rule_id", "host_id", "resource_key" },
                unique: true,
                filter: "status = 'Firing'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "alert_rules");
        }
    }
}
