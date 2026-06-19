using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thaliak.Common.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPatchAlertBatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notification_discovery_type",
                table: "patches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "notification_queued_at_utc",
                table: "patches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "notification_sent_at_utc",
                table: "patches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_patches_notification_queued_at_utc",
                table: "patches",
                column: "notification_queued_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_patches_notification_sent_at_utc",
                table: "patches",
                column: "notification_sent_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_patches_notification_queued_at_utc",
                table: "patches");

            migrationBuilder.DropIndex(
                name: "ix_patches_notification_sent_at_utc",
                table: "patches");

            migrationBuilder.DropColumn(
                name: "notification_discovery_type",
                table: "patches");

            migrationBuilder.DropColumn(
                name: "notification_queued_at_utc",
                table: "patches");

            migrationBuilder.DropColumn(
                name: "notification_sent_at_utc",
                table: "patches");
        }
    }
}
