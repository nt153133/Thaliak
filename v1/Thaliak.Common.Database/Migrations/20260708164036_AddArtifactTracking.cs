using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Thaliak.Common.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artifacts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    region = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    repository_slug = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    version_string = table.Column<string>(type: "TEXT", nullable: false),
                    relative_path = table.Column<string>(type: "TEXT", nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    error = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ready_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    notified_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artifacts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artifacts_kind_region_version_string",
                table: "artifacts",
                columns: new[] { "kind", "region", "version_string" });

            migrationBuilder.CreateIndex(
                name: "ix_artifacts_kind_repository_slug_version_string",
                table: "artifacts",
                columns: new[] { "kind", "repository_slug", "version_string" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifacts");
        }
    }
}
