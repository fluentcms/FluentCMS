using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class EntityFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntityFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    SourceReference = table.Column<string>(type: "TEXT", nullable: true),
                    Owner = table.Column<Guid>(type: "TEXT", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastVerified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Unsourced = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityFacts", x => x.Id);
                    table.CheckConstraint("CK_EntityFacts_Unsourced", "\"Unsourced\" = (\"SourceKind\" IS NULL)");
                    table.CheckConstraint("CK_EntityFacts_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_EntityFacts_Entries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntityFacts_EntryId_Version_Field",
                table: "EntityFacts",
                columns: new[] { "EntryId", "Version", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityFacts_LastVerified",
                table: "EntityFacts",
                column: "LastVerified");

            migrationBuilder.CreateIndex(
                name: "IX_EntityFacts_Unsourced",
                table: "EntityFacts",
                column: "Unsourced");

            migrationBuilder.CreateIndex(
                name: "IX_EntityFacts_ValidUntil",
                table: "EntityFacts",
                column: "ValidUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntityFacts");
        }
    }
}
