using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Entries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LatestVersion = table.Column<int>(type: "integer", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                    table.CheckConstraint("CK_Entries_LatestVersion", "\"LatestVersion\" >= 1");
                    table.CheckConstraint("CK_Entries_PublishedVersion", "\"PublishedVersion\" IS NULL OR (\"PublishedVersion\" >= 1 AND \"PublishedVersion\" <= \"LatestVersion\")");
                    table.ForeignKey(
                        name: "FK_Entries_ContentTypes_ContentTypeId",
                        column: x => x.ContentTypeId,
                        principalTable: "ContentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Entries_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EntryVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ContentTypeVersion = table.Column<int>(type: "integer", nullable: false),
                    Fields = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryVersions", x => x.Id);
                    table.CheckConstraint("CK_EntryVersions_ContentTypeVersion", "\"ContentTypeVersion\" >= 1");
                    table.CheckConstraint("CK_EntryVersions_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_EntryVersions_Entries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entries_ContentTypeId",
                table: "Entries",
                column: "ContentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Entries_SiteId_ContentTypeId",
                table: "Entries",
                columns: new[] { "SiteId", "ContentTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_EntryVersions_EntryId_Version",
                table: "EntryVersions",
                columns: new[] { "EntryId", "Version" },
                unique: true);

            // Versions are never overwritten (CNT-02). A trigger binds every role, the table's owner included.
            migrationBuilder.Sql("""
                CREATE FUNCTION "EntryVersions_RefuseUpdate"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Entry versions are never overwritten (CNT-02).';
                END;
                $$;

                CREATE TRIGGER "TR_EntryVersions_NoUpdate" BEFORE UPDATE ON "EntryVersions"
                    FOR EACH ROW EXECUTE FUNCTION "EntryVersions_RefuseUpdate"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryVersions");

            migrationBuilder.DropTable(
                name: "Entries");

            migrationBuilder.Sql("""DROP FUNCTION "EntryVersions_RefuseUpdate"();""");
        }
    }
}
