using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thaliak.Common.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_states",
                columns: table => new
                {
                    repository_id = table.Column<int>(type: "INTEGER", nullable: false),
                    last_applied_patch_id = table.Column<int>(type: "INTEGER", nullable: true),
                    installed_version = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    last_attempted_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_completed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installation_states", x => x.repository_id);
                    table.ForeignKey(
                        name: "fk_installation_states_patches_last_applied_patch_id",
                        column: x => x.last_applied_patch_id,
                        principalTable: "patches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_installation_states_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_installation_states_last_applied_patch_id",
                table: "installation_states",
                column: "last_applied_patch_id");

            migrationBuilder.CreateIndex(
                name: "ix_installation_states_status",
                table: "installation_states",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_states");
        }
    }
}
