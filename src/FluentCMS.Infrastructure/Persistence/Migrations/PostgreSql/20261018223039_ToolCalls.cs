using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class ToolCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AgentCredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tool = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TaskId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolCalls_AgentCredentials_AgentCredentialId",
                        column: x => x.AgentCredentialId,
                        principalTable: "AgentCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolCalls_AgentCredentialId_Timestamp",
                table: "ToolCalls",
                columns: new[] { "AgentCredentialId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolCalls");
        }
    }
}
