using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InitiatorKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AgentCredentialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Sources = table.Column<List<string>>(type: "text[]", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousVersionId = table.Column<Guid>(type: "uuid", nullable: true)
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

            // Append-only (AUD-N2, ADR-0005). The role applying this migration owns the table, so the revoke binds it too;
            // a superuser bypasses privilege checks, so the deploying role must not be one for this to hold.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE, TRUNCATE ON \"AuditEvents\" FROM PUBLIC, CURRENT_USER;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
