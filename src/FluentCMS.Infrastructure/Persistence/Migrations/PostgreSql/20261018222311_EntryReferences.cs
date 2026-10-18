using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class EntryReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntryReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetField = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryReferences", x => x.Id);
                    table.CheckConstraint("CK_EntryReferences_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_EntryReferences_Entries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "Entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntryReferences_EntryId_Version",
                table: "EntryReferences",
                columns: new[] { "EntryId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_EntryReferences_TargetKind_TargetId_TargetField",
                table: "EntryReferences",
                columns: new[] { "TargetKind", "TargetId", "TargetField" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryReferences");
        }
    }
}
