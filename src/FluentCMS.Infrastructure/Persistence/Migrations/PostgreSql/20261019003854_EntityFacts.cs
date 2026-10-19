using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SourceReference = table.Column<string>(type: "text", nullable: true),
                    Owner = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastVerified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Unsourced = table.Column<bool>(type: "boolean", nullable: false)
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
