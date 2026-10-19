using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MediaAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LatestVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedVersion = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.CheckConstraint("CK_MediaAssets_LatestVersion", "\"LatestVersion\" >= 1");
                    table.CheckConstraint("CK_MediaAssets_PublishedVersion", "\"PublishedVersion\" IS NULL OR (\"PublishedVersion\" >= 1 AND \"PublishedVersion\" <= \"LatestVersion\")");
                    table.ForeignKey(
                        name: "FK_MediaAssets_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaAssetVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 127, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Storage = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Folder = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssetVersions", x => x.Id);
                    table.CheckConstraint("CK_MediaAssetVersions_Length", "\"Length\" > 0");
                    table.CheckConstraint("CK_MediaAssetVersions_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_MediaAssetVersions_MediaAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_SiteId",
                table: "MediaAssets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssetVersions_AssetId_Version",
                table: "MediaAssetVersions",
                columns: new[] { "AssetId", "Version" },
                unique: true);

            // Versions are never overwritten (CNT-02); dropping the table drops the trigger with it.
            migrationBuilder.Sql("""
                CREATE TRIGGER "TR_MediaAssetVersions_NoUpdate" BEFORE UPDATE ON "MediaAssetVersions"
                BEGIN
                    SELECT RAISE(ABORT, 'Media asset versions are never overwritten (CNT-02).');
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssetVersions_Folder",
                table: "MediaAssetVersions",
                column: "Folder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaAssetVersions");

            migrationBuilder.DropTable(
                name: "MediaAssets");
        }
    }
}
