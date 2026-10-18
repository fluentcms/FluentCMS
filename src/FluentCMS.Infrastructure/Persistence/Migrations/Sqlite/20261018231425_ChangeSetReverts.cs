using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ChangeSetReverts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RevertsChangeSetId",
                table: "ChangeSets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeSets_RevertsChangeSetId",
                table: "ChangeSets",
                column: "RevertsChangeSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeSets_ChangeSets_RevertsChangeSetId",
                table: "ChangeSets",
                column: "RevertsChangeSetId",
                principalTable: "ChangeSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChangeSets_ChangeSets_RevertsChangeSetId",
                table: "ChangeSets");

            migrationBuilder.DropIndex(
                name: "IX_ChangeSets_RevertsChangeSetId",
                table: "ChangeSets");

            migrationBuilder.DropColumn(
                name: "RevertsChangeSetId",
                table: "ChangeSets");
        }
    }
}
