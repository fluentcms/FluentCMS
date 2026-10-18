using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    InitiatorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    InitiatorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InitiatorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AgentCredentialId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PromptHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Sources = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreviousVersionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_AgentCredentialId", "(\"InitiatorKind\" = 'agent') = (\"AgentCredentialId\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AgentCredentialId",
                table: "AuditEvents",
                column: "AgentCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ChangeSetId",
                table: "AuditEvents",
                column: "ChangeSetId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SubjectType_SubjectId",
                table: "AuditEvents",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Timestamp",
                table: "AuditEvents",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
