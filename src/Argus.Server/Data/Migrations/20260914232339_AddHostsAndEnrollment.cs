using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHostsAndEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrollment_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollment_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_enrollment_tokens_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    hostname = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    machine_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    os_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    os_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    kernel_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    architecture = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    cpu_model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    cpu_cores = table.Column<int>(type: "integer", nullable: true),
                    cpu_logical_processors = table.Column<int>(type: "integer", nullable: false),
                    memory_total_bytes = table.Column<long>(type: "bigint", nullable: false),
                    boot_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip_addresses = table.Column<List<string>>(type: "text[]", nullable: false),
                    agent_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    agent_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inventory_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hosts", x => x.id);
                    table.ForeignKey(
                        name: "fk_hosts_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_tokens_owner_id",
                table: "enrollment_tokens",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_tokens_token_hash",
                table: "enrollment_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hosts_agent_key_hash",
                table: "hosts",
                column: "agent_key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hosts_owner_id_machine_id",
                table: "hosts",
                columns: new[] { "owner_id", "machine_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hosts_tags",
                table: "hosts",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollment_tokens");

            migrationBuilder.DropTable(
                name: "hosts");
        }
    }
}
