using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class ProposedMediaVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProposedMediaVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProposedMediaVersions", x => x.Id);
                    table.CheckConstraint("CK_ProposedMediaVersions_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_ProposedMediaVersions_ChangeSets_ChangeSetId",
                        column: x => x.ChangeSetId,
                        principalTable: "ChangeSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProposedMediaVersions_MediaAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProposedMediaVersions_AssetId_Version_ChangeSetId",
                table: "ProposedMediaVersions",
                columns: new[] { "AssetId", "Version", "ChangeSetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProposedMediaVersions_ChangeSetId",
                table: "ProposedMediaVersions",
                column: "ChangeSetId");

            // The versions sets propose now; an earlier one a set has moved on from is no longer known.
            migrationBuilder.Sql("""
                INSERT INTO "ProposedMediaVersions" ("Id", "ChangeSetId", "AssetId", "Version")
                SELECT "Id", "ChangeSetId", "ItemId", "ProposedVersion" FROM "ChangeSetOperations"
                WHERE "ItemKind" = 'Media' AND "ProposedVersion" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProposedMediaVersions");
        }
    }
}
