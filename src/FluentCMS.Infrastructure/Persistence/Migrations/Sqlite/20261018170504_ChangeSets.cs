using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ChangeSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", nullable: false),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    InitiatorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    InitiatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InitiatorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeSets", x => x.Id);
                    table.CheckConstraint("CK_ChangeSets_RejectionReason", "(\"State\" = 'Rejected') = (\"RejectionReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ChangeSets_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeSetOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ItemKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ProposedVersion = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeSetOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeSetOperations_ChangeSets_ChangeSetId",
                        column: x => x.ChangeSetId,
                        principalTable: "ChangeSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSetOperations_ChangeSetId_ItemKind_ItemId",
                table: "ChangeSetOperations",
                columns: new[] { "ChangeSetId", "ItemKind", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSetOperations_ChangeSetId_Position",
                table: "ChangeSetOperations",
                columns: new[] { "ChangeSetId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSetOperations_ItemKind_ItemId",
                table: "ChangeSetOperations",
                columns: new[] { "ItemKind", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSets_InitiatorKind_InitiatorId_SessionId",
                table: "ChangeSets",
                columns: new[] { "InitiatorKind", "InitiatorId", "SessionId" },
                unique: true,
                filter: "\"State\" = 'Open' AND \"SessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSets_SiteId",
                table: "ChangeSets",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSets_State",
                table: "ChangeSets",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeSetOperations");

            migrationBuilder.DropTable(
                name: "ChangeSets");
        }
    }
}
