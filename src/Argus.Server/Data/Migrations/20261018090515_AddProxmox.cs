using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProxmox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "proxmox_checks",
                columns: table => new
                {
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    node = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    problem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actions_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    guests = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proxmox_checks", x => x.host_id);
                    table.ForeignKey(
                        name: "fk_proxmox_checks_hosts_host_id",
                        column: x => x.host_id,
                        principalTable: "hosts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proxmox_checks");
        }
    }
}
