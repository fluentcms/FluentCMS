using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ChangeSetApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequiredApprovals",
                table: "ChangeSets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ChangeSetApprovals",
                columns: table => new
                {
                    ChangeSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApproverName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeSetApprovals", x => new { x.ChangeSetId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChangeSetApprovals_ChangeSets_ChangeSetId",
                        column: x => x.ChangeSetId,
                        principalTable: "ChangeSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChangeSets_RequiredApprovals",
                table: "ChangeSets",
                sql: "\"RequiredApprovals\" BETWEEN 1 AND 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeSetApprovals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChangeSets_RequiredApprovals",
                table: "ChangeSets");

            migrationBuilder.DropColumn(
                name: "RequiredApprovals",
                table: "ChangeSets");
        }
    }
}
