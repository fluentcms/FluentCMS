using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class RuleCompilations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuleCheckProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleVersion = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    CheckName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Definition = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CheckId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleCheckProposals", x => x.Id);
                    table.CheckConstraint("CK_RuleCheckProposals_Number", "\"Number\" >= 1");
                    table.CheckConstraint("CK_RuleCheckProposals_RuleVersion", "\"RuleVersion\" >= 1");
                    table.ForeignKey(
                        name: "FK_RuleCheckProposals_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RuleCheckProposals_Rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RuleCompilations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleCompilations", x => x.Id);
                    table.CheckConstraint("CK_RuleCompilations_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_RuleCompilations_Rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleCheckProposals_CheckId",
                table: "RuleCheckProposals",
                column: "CheckId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleCheckProposals_RuleId_Number",
                table: "RuleCheckProposals",
                columns: new[] { "RuleId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleCheckProposals_RuleId_RuleVersion",
                table: "RuleCheckProposals",
                columns: new[] { "RuleId", "RuleVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_RuleCompilations_RuleId_Version",
                table: "RuleCompilations",
                columns: new[] { "RuleId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuleCheckProposals");

            migrationBuilder.DropTable(
                name: "RuleCompilations");
        }
    }
}
