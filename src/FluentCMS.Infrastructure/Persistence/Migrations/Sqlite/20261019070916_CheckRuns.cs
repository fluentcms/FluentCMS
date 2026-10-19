using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CheckRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Problems = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckRuns", x => x.Id);
                    table.CheckConstraint("CK_CheckRuns_Number", "\"Number\" >= 1");
                    table.ForeignKey(
                        name: "FK_CheckRuns_ChangeSets_ChangeSetId",
                        column: x => x.ChangeSetId,
                        principalTable: "ChangeSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CheckId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Check = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CheckVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CheckType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Findings = table.Column<string>(type: "TEXT", nullable: false),
                    ModelCallId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PromptHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckResults_CheckRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "CheckRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckResults_RunId_Position",
                table: "CheckResults",
                columns: new[] { "RunId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckRuns_ChangeSetId_Number",
                table: "CheckRuns",
                columns: new[] { "ChangeSetId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckResults");

            migrationBuilder.DropTable(
                name: "CheckRuns");
        }
    }
}
