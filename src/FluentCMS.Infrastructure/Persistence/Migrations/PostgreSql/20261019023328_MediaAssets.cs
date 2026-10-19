using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    LatestVersion = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: true)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Storage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Folder = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    AltText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Caption = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            // Versions are never overwritten (CNT-02). A trigger binds every role, the table's owner included.
            migrationBuilder.Sql("""
                CREATE FUNCTION "MediaAssetVersions_RefuseUpdate"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Media asset versions are never overwritten (CNT-02).';
                END;
                $$;

                CREATE TRIGGER "TR_MediaAssetVersions_NoUpdate" BEFORE UPDATE ON "MediaAssetVersions"
                    FOR EACH ROW EXECUTE FUNCTION "MediaAssetVersions_RefuseUpdate"();
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

            migrationBuilder.Sql("""DROP FUNCTION "MediaAssetVersions_RefuseUpdate"();""");
        }
    }
}
