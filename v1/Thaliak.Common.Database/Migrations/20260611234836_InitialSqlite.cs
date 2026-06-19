using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Thaliak.Common.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    username = table.Column<string>(type: "TEXT", nullable: false),
                    password = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "discord_hooks",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    url = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discord_hooks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sha1 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 40, nullable: false),
                    size = table.Column<ulong>(type: "INTEGER", nullable: false),
                    last_used = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => new { x.name, x.sha1 });
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    icon = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "game_versions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    service_id = table.Column<int>(type: "INTEGER", nullable: false),
                    version_name = table.Column<string>(type: "TEXT", nullable: false),
                    hotfix_level = table.Column<int>(type: "INTEGER", nullable: false),
                    marketing_name = table.Column<string>(type: "TEXT", nullable: true),
                    patch_info_url = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_game_versions_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repositories",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    service_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    slug = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repositories", x => x.id);
                    table.ForeignKey(
                        name: "fk_repositories_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_repositories",
                columns: table => new
                {
                    applicable_accounts_id = table.Column<int>(type: "INTEGER", nullable: false),
                    applicable_repositories_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_repositories", x => new { x.applicable_accounts_id, x.applicable_repositories_id });
                    table.ForeignKey(
                        name: "fk_account_repositories_accounts_applicable_accounts_id",
                        column: x => x.applicable_accounts_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_account_repositories_repositories_applicable_repositories_id",
                        column: x => x.applicable_repositories_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expansion_repository_mappings",
                columns: table => new
                {
                    game_repository_id = table.Column<int>(type: "INTEGER", nullable: false),
                    expansion_id = table.Column<int>(type: "INTEGER", nullable: false),
                    expansion_repository_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expansion_repository_mappings", x => new { x.game_repository_id, x.expansion_id, x.expansion_repository_id });
                    table.ForeignKey(
                        name: "fk_expansion_repository_mappings_repositories_expansion_repository_id",
                        column: x => x.expansion_repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_expansion_repository_mappings_repositories_game_repository_id",
                        column: x => x.game_repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repo_versions",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    version_string = table.Column<string>(type: "TEXT", nullable: false),
                    repository_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repo_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_repo_versions_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_version_repo_versions",
                columns: table => new
                {
                    game_versions_id = table.Column<int>(type: "INTEGER", nullable: false),
                    repo_versions_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_version_repo_versions", x => new { x.game_versions_id, x.repo_versions_id });
                    table.ForeignKey(
                        name: "fk_game_version_repo_versions_game_versions_game_versions_id",
                        column: x => x.game_versions_id,
                        principalTable: "game_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_version_repo_versions_repo_versions_repo_versions_id",
                        column: x => x.repo_versions_id,
                        principalTable: "repo_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patches",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    repo_version_id = table.Column<int>(type: "INTEGER", nullable: false),
                    remote_origin_path = table.Column<string>(type: "TEXT", nullable: false),
                    local_storage_path = table.Column<string>(type: "TEXT", nullable: false),
                    first_seen = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_seen = table.Column<DateTime>(type: "TEXT", nullable: true),
                    first_offered = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_offered = table.Column<DateTime>(type: "TEXT", nullable: true),
                    size = table.Column<long>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    hash_type = table.Column<string>(type: "TEXT", nullable: true),
                    hash_block_size = table.Column<long>(type: "INTEGER", nullable: true),
                    hashes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patches", x => x.id);
                    table.ForeignKey(
                        name: "fk_patches_repo_versions_repo_version_id",
                        column: x => x.repo_version_id,
                        principalTable: "repo_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upgrade_paths",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    repository_id = table.Column<int>(type: "INTEGER", nullable: false),
                    repo_version_id = table.Column<int>(type: "INTEGER", nullable: false),
                    previous_repo_version_id = table.Column<int>(type: "INTEGER", nullable: true),
                    first_offered = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_offered = table.Column<DateTime>(type: "TEXT", nullable: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upgrade_paths", x => x.id);
                    table.ForeignKey(
                        name: "fk_upgrade_paths_repo_versions_previous_repo_version_id",
                        column: x => x.previous_repo_version_id,
                        principalTable: "repo_versions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_upgrade_paths_repo_versions_repo_version_id",
                        column: x => x.repo_version_id,
                        principalTable: "repo_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_upgrade_paths_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "version_files",
                columns: table => new
                {
                    versions_id = table.Column<int>(type: "INTEGER", nullable: false),
                    files_name = table.Column<string>(type: "TEXT", nullable: false),
                    files_sha1 = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_version_files", x => new { x.versions_id, x.files_name, x.files_sha1 });
                    table.ForeignKey(
                        name: "fk_version_files_files_files_name_files_sha1",
                        columns: x => new { x.files_name, x.files_sha1 },
                        principalTable: "files",
                        principalColumns: new[] { "name", "sha1" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_version_files_repo_versions_versions_id",
                        column: x => x.versions_id,
                        principalTable: "repo_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "services",
                columns: new[] { "id", "icon", "name" },
                values: new object[,]
                {
                    { 1, "🇺🇳", "FFXIV Global" },
                    { 2, "🇰🇷", "FFXIV Korea" },
                    { 3, "🇨🇳", "FFXIV China" },
                    { 4, "🇹🇼", "FFXIV Traditional Chinese" }
                });

            migrationBuilder.InsertData(
                table: "repositories",
                columns: new[] { "id", "description", "name", "service_id", "slug" },
                values: new object[,]
                {
                    { 1, "FFXIV Global/JP - Retail - Boot - Win32", "ffxivneo/win32/release/boot", 1, "2b5cbc63" },
                    { 2, "FFXIV Global/JP - Retail - Base Game - Win32", "ffxivneo/win32/release/game", 1, "4e9a232b" },
                    { 3, "FFXIV Global/JP - Retail - ex1 (Heavensward) - Win32", "ffxivneo/win32/release/ex1", 1, "6b936f08" },
                    { 4, "FFXIV Global/JP - Retail - ex2 (Stormblood) - Win32", "ffxivneo/win32/release/ex2", 1, "f29a3eb2" },
                    { 5, "FFXIV Global/JP - Retail - ex3 (Shadowbringers) - Win32", "ffxivneo/win32/release/ex3", 1, "859d0e24" },
                    { 6, "FFXIV Global/JP - Retail - ex4 (Endwalker) - Win32", "ffxivneo/win32/release/ex4", 1, "1bf99b87" },
                    { 7, "FFXIV Korea - Retail - Base Game - Win32", "actoz/win32/release_ko/game", 2, "de199059" },
                    { 8, "FFXIV Korea - Retail - ex1 (Heavensward) - Win32", "actoz/win32/release_ko/ex1", 2, "573d8c07" },
                    { 9, "FFXIV Korea - Retail - ex2 (Stormblood) - Win32", "actoz/win32/release_ko/ex2", 2, "ce34ddbd" },
                    { 10, "FFXIV Korea - Retail - ex3 (Shadowbringers) - Win32", "actoz/win32/release_ko/ex3", 2, "b933ed2b" },
                    { 11, "FFXIV Korea - Retail - ex4 (Endwalker) - Win32", "actoz/win32/release_ko/ex4", 2, "27577888" },
                    { 12, "FFXIV China - Retail - Base Game - Win32", "shanda/win32/release_chs/game", 3, "c38effbc" },
                    { 13, "FFXIV China - Retail - ex1 (Heavensward) - Win32", "shanda/win32/release_chs/ex1", 3, "77420d17" },
                    { 14, "FFXIV China - Retail - ex2 (Stormblood) - Win32", "shanda/win32/release_chs/ex2", 3, "ee4b5cad" },
                    { 15, "FFXIV China - Retail - ex3 (Shadowbringers) - Win32", "shanda/win32/release_chs/ex3", 3, "994c6c3b" },
                    { 16, "FFXIV China - Retail - ex4 (Endwalker) - Win32", "shanda/win32/release_chs/ex4", 3, "0728f998" },
                    { 17, "FFXIV Global/JP - Retail - ex5 (Dawntrail) - Win32", "ffxivneo/win32/release/ex5", 1, "6cfeab11" },
                    { 18, "FFXIV Korea - Retail - ex5 (Dawntrail) - Win32", "actoz/win32/release_ko/ex5", 2, "5050481e" },
                    { 19, "FFXIV China - Retail - ex5 (Dawntrail) - Win32", "shanda/win32/release_chs/ex5", 3, "702fc90e" },
                    { 20, "FFXIV Traditional Chinese - Retail - Base Game - Win32", "traditional_chinese/win32/release/game", 4, "961a4536" },
                    { 21, "FFXIV Traditional Chinese - Retail - ex1 (Heavensward) - Win32", "traditional_chinese/win32/release/ex1", 4, "e6dea8a0" },
                    { 22, "FFXIV Traditional Chinese - Retail - ex2 (Stormblood) - Win32", "traditional_chinese/win32/release/ex2", 4, "7fd7f91a" },
                    { 23, "FFXIV Traditional Chinese - Retail - ex3 (Shadowbringers) - Win32", "traditional_chinese/win32/release/ex3", 4, "08d0c98c" },
                    { 24, "FFXIV Traditional Chinese - Retail - ex4 (Endwalker) - Win32", "traditional_chinese/win32/release/ex4", 4, "96b45c2f" },
                    { 25, "FFXIV Traditional Chinese - Retail - ex5 (Dawntrail) - Win32", "traditional_chinese/win32/release/ex5", 4, "e1b36cb9" }
                });

            migrationBuilder.InsertData(
                table: "expansion_repository_mappings",
                columns: new[] { "expansion_id", "expansion_repository_id", "game_repository_id" },
                values: new object[,]
                {
                    { 0, 2, 2 },
                    { 1, 3, 2 },
                    { 2, 4, 2 },
                    { 3, 5, 2 },
                    { 4, 6, 2 },
                    { 5, 17, 2 },
                    { 0, 7, 7 },
                    { 1, 8, 7 },
                    { 2, 9, 7 },
                    { 3, 10, 7 },
                    { 4, 11, 7 },
                    { 5, 18, 7 },
                    { 0, 12, 12 },
                    { 1, 13, 12 },
                    { 2, 14, 12 },
                    { 3, 15, 12 },
                    { 4, 16, 12 },
                    { 5, 19, 12 },
                    { 0, 20, 20 },
                    { 1, 21, 20 },
                    { 2, 22, 20 },
                    { 3, 23, 20 },
                    { 4, 24, 20 },
                    { 5, 25, 20 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_repositories_applicable_repositories_id",
                table: "account_repositories",
                column: "applicable_repositories_id");

            migrationBuilder.CreateIndex(
                name: "ix_expansion_repository_mappings_expansion_repository_id",
                table: "expansion_repository_mappings",
                column: "expansion_repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_last_used",
                table: "files",
                column: "last_used");

            migrationBuilder.CreateIndex(
                name: "ix_game_version_repo_versions_repo_versions_id",
                table: "game_version_repo_versions",
                column: "repo_versions_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_versions_hotfix_level",
                table: "game_versions",
                column: "hotfix_level");

            migrationBuilder.CreateIndex(
                name: "ix_game_versions_service_id",
                table: "game_versions",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_versions_version_name",
                table: "game_versions",
                column: "version_name");

            migrationBuilder.CreateIndex(
                name: "ix_patches_repo_version_id",
                table: "patches",
                column: "repo_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_repo_versions_repository_id",
                table: "repo_versions",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_repo_versions_version_string",
                table: "repo_versions",
                column: "version_string");

            migrationBuilder.CreateIndex(
                name: "ix_repositories_service_id",
                table: "repositories",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_repositories_slug",
                table: "repositories",
                column: "slug");

            migrationBuilder.CreateIndex(
                name: "ix_upgrade_paths_previous_repo_version_id",
                table: "upgrade_paths",
                column: "previous_repo_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_upgrade_paths_repo_version_id",
                table: "upgrade_paths",
                column: "repo_version_id",
                unique: true,
                filter: "\"previous_repo_version_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_upgrade_paths_repo_version_id_previous_repo_version_id",
                table: "upgrade_paths",
                columns: new[] { "repo_version_id", "previous_repo_version_id" },
                unique: true,
                filter: "\"previous_repo_version_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_upgrade_paths_repository_id",
                table: "upgrade_paths",
                column: "repository_id");

            migrationBuilder.CreateIndex(
                name: "ix_version_files_files_name_files_sha1",
                table: "version_files",
                columns: new[] { "files_name", "files_sha1" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_repositories");

            migrationBuilder.DropTable(
                name: "discord_hooks");

            migrationBuilder.DropTable(
                name: "expansion_repository_mappings");

            migrationBuilder.DropTable(
                name: "game_version_repo_versions");

            migrationBuilder.DropTable(
                name: "patches");

            migrationBuilder.DropTable(
                name: "upgrade_paths");

            migrationBuilder.DropTable(
                name: "version_files");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "game_versions");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "repo_versions");

            migrationBuilder.DropTable(
                name: "repositories");

            migrationBuilder.DropTable(
                name: "services");
        }
    }
}
