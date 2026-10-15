using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPowerActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "agent_commands",
                table: "hosts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "mac_addresses",
                table: "hosts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "networks",
                table: "hosts",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<string>(
                name: "wake_address",
                table: "hosts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wake_mac_address",
                table: "hosts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "shutdown_host_ids",
                table: "alert_rules",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<bool>(
                name: "wake_on_resolve",
                table: "alert_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "power_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_power_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_power_actions_alert_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "alert_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_power_actions_hosts_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_power_actions_host_id_at",
                table: "power_actions",
                columns: new[] { "host_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_power_actions_rule_id",
                table: "power_actions",
                column: "rule_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "power_actions");

            migrationBuilder.DropColumn(
                name: "agent_commands",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "mac_addresses",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "networks",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "wake_address",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "wake_mac_address",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "shutdown_host_ids",
                table: "alert_rules");

            migrationBuilder.DropColumn(
                name: "wake_on_resolve",
                table: "alert_rules");
        }
    }
}
