using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argus.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "daily_report",
                table: "notification_channels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "daily_report_sent_for",
                table: "notification_channels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "weekly_report",
                table: "notification_channels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "weekly_report_sent_for",
                table: "notification_channels",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "daily_report",
                table: "notification_channels");

            migrationBuilder.DropColumn(
                name: "daily_report_sent_for",
                table: "notification_channels");

            migrationBuilder.DropColumn(
                name: "weekly_report",
                table: "notification_channels");

            migrationBuilder.DropColumn(
                name: "weekly_report_sent_for",
                table: "notification_channels");
        }
    }
}
