using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <summary>
    /// Anomaly rules: a rule's condition (existing rules keep their fixed thresholds) and, on alerts,
    /// the usual level an anomaly was measured against.
    /// </summary>
    public partial class AddAnomalyRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "baseline",
                table: "alerts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condition",
                table: "alerts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Threshold");

            migrationBuilder.AddColumn<string>(
                name: "condition",
                table: "alert_rules",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Threshold");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "baseline",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "condition",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "condition",
                table: "alert_rules");
        }
    }
}
