using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thaliak.Common.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalExpansionSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "accounts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Routine");

            migrationBuilder.CreateTable(
                name: "expansion_sweep_attempts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    trigger_key = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    trigger_repo_version_id = table.Column<int>(type: "INTEGER", nullable: false),
                    trigger = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    detected_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    max_expansion = table.Column<int>(type: "INTEGER", nullable: true),
                    discovered_patch_count = table.Column<int>(type: "INTEGER", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expansion_sweep_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_expansion_sweep_attempts_repo_versions_trigger_repo_version_id",
                        column: x => x.trigger_repo_version_id,
                        principalTable: "repo_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_purpose",
                table: "accounts",
                column: "purpose",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expansion_sweep_attempts_status",
                table: "expansion_sweep_attempts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_expansion_sweep_attempts_trigger_key",
                table: "expansion_sweep_attempts",
                column: "trigger_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expansion_sweep_attempts_trigger_repo_version_id",
                table: "expansion_sweep_attempts",
                column: "trigger_repo_version_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expansion_sweep_attempts");

            migrationBuilder.DropIndex(
                name: "ix_accounts_purpose",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "purpose",
                table: "accounts");
        }
    }
}
