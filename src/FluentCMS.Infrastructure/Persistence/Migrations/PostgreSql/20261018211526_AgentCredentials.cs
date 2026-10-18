using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AgentCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    KeyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Sites = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    Types = table.Column<List<string>>(type: "text[]", nullable: true),
                    Fields = table.Column<List<string>>(type: "text[]", nullable: true),
                    Operations = table.Column<string[]>(type: "text[]", nullable: false),
                    CallsPerPeriod = table.Column<int>(type: "integer", nullable: false),
                    CallPeriod = table.Column<TimeSpan>(type: "interval", nullable: false),
                    LlmSpendPerPeriod = table.Column<decimal>(type: "numeric", nullable: false),
                    LlmSpendPeriod = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ItemsPerChangeSet = table.Column<int>(type: "integer", nullable: false),
                    ChangeSetsPerDay = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentCredentials", x => x.Id);
                    table.CheckConstraint("CK_AgentCredentials_Caps", "\"CallsPerPeriod\" >= 1 AND \"ItemsPerChangeSet\" >= 1 AND \"ChangeSetsPerDay\" >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentCredentials_KeyHash",
                table: "AgentCredentials",
                column: "KeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentCredentials");
        }
    }
}
