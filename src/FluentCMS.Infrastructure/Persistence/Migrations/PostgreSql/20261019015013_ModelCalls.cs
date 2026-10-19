using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class ModelCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InitiatorKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgentCredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: true),
                    Response = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelCalls_AgentCredentials_AgentCredentialId",
                        column: x => x.AgentCredentialId,
                        principalTable: "AgentCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCalls_AgentCredentialId_Timestamp",
                table: "ModelCalls",
                columns: new[] { "AgentCredentialId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelCalls_PromptHash",
                table: "ModelCalls",
                column: "PromptHash");

            migrationBuilder.CreateIndex(
                name: "IX_ModelCalls_Timestamp",
                table: "ModelCalls",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelCalls");
        }
    }
}
