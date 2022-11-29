using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResLogger2.Common.ServerDatabase.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameVersions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<uint>(type: "INTEGER", nullable: false),
                    Month = table.Column<uint>(type: "INTEGER", nullable: false),
                    Day = table.Column<uint>(type: "INTEGER", nullable: false),
                    Part = table.Column<uint>(type: "INTEGER", nullable: false),
                    Revision = table.Column<uint>(type: "INTEGER", nullable: false),
                    IsSpecial = table.Column<bool>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Index1StagingEntries",
                columns: table => new
                {
                    IndexId = table.Column<uint>(type: "INTEGER", nullable: false),
                    FolderHash = table.Column<uint>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<uint>(type: "INTEGER", nullable: false),
                    FirstSeenId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Index1StagingEntries", x => new { x.IndexId, x.FolderHash, x.FileHash });
                    table.ForeignKey(
                        name: "FK_Index1StagingEntries_GameVersions_FirstSeenId",
                        column: x => x.FirstSeenId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Index1StagingEntries_GameVersions_LastSeenId",
                        column: x => x.LastSeenId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Index2StagingEntries",
                columns: table => new
                {
                    IndexId = table.Column<uint>(type: "INTEGER", nullable: false),
                    FullHash = table.Column<uint>(type: "INTEGER", nullable: false),
                    FirstSeenId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Index2StagingEntries", x => new { x.IndexId, x.FullHash });
                    table.ForeignKey(
                        name: "FK_Index2StagingEntries_GameVersions_FirstSeenId",
                        column: x => x.FirstSeenId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Index2StagingEntries_GameVersions_LastSeenId",
                        column: x => x.LastSeenId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LatestIndexes",
                columns: table => new
                {
                    IndexId = table.Column<uint>(type: "INTEGER", nullable: false),
                    GameVersionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatestIndexes", x => x.IndexId);
                    table.ForeignKey(
                        name: "FK_LatestIndexes_GameVersions_GameVersionId",
                        column: x => x.GameVersionId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LatestProcessedVersions",
                columns: table => new
                {
                    Repo = table.Column<string>(type: "TEXT", nullable: false),
                    VersionId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LatestProcessedVersions", x => x.Repo);
                    table.ForeignKey(
                        name: "FK_LatestProcessedVersions_GameVersions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Paths",
                columns: table => new
                {
                    IndexId = table.Column<uint>(type: "INTEGER", nullable: false),
                    FolderHash = table.Column<uint>(type: "INTEGER", nullable: false),
                    FileHash = table.Column<uint>(type: "INTEGER", nullable: false),
                    FullHash = table.Column<uint>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paths", x => new { x.IndexId, x.FolderHash, x.FileHash, x.FullHash });
                    table.ForeignKey(
                        name: "FK_Paths_GameVersions_FirstSeenId",
                        column: x => x.FirstSeenId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Paths_GameVersions_LastSeenId",
                        column: x => x.LastSeenId,
                        principalTable: "GameVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameVersions_Id",
                table: "GameVersions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Index1StagingEntries_FirstSeenId",
                table: "Index1StagingEntries",
                column: "FirstSeenId");

            migrationBuilder.CreateIndex(
                name: "IX_Index1StagingEntries_IndexId_FolderHash_FileHash",
                table: "Index1StagingEntries",
                columns: new[] { "IndexId", "FolderHash", "FileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Index1StagingEntries_LastSeenId",
                table: "Index1StagingEntries",
                column: "LastSeenId");

            migrationBuilder.CreateIndex(
                name: "IX_Index2StagingEntries_FirstSeenId",
                table: "Index2StagingEntries",
                column: "FirstSeenId");

            migrationBuilder.CreateIndex(
                name: "IX_Index2StagingEntries_IndexId_FullHash",
                table: "Index2StagingEntries",
                columns: new[] { "IndexId", "FullHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Index2StagingEntries_LastSeenId",
                table: "Index2StagingEntries",
                column: "LastSeenId");

            migrationBuilder.CreateIndex(
                name: "IX_LatestIndexes_GameVersionId",
                table: "LatestIndexes",
                column: "GameVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LatestProcessedVersions_VersionId",
                table: "LatestProcessedVersions",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Paths_FirstSeenId",
                table: "Paths",
                column: "FirstSeenId");

            migrationBuilder.CreateIndex(
                name: "IX_Paths_IndexId",
                table: "Paths",
                column: "IndexId");

            migrationBuilder.CreateIndex(
                name: "IX_Paths_IndexId_FolderHash_FileHash",
                table: "Paths",
                columns: new[] { "IndexId", "FolderHash", "FileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Paths_IndexId_FolderHash_FileHash_FullHash",
                table: "Paths",
                columns: new[] { "IndexId", "FolderHash", "FileHash", "FullHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Paths_IndexId_FullHash",
                table: "Paths",
                columns: new[] { "IndexId", "FullHash" });

            migrationBuilder.CreateIndex(
                name: "IX_Paths_LastSeenId",
                table: "Paths",
                column: "LastSeenId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Index1StagingEntries");

            migrationBuilder.DropTable(
                name: "Index2StagingEntries");

            migrationBuilder.DropTable(
                name: "LatestIndexes");

            migrationBuilder.DropTable(
                name: "LatestProcessedVersions");

            migrationBuilder.DropTable(
                name: "Paths");

            migrationBuilder.DropTable(
                name: "GameVersions");
        }
    }
}
