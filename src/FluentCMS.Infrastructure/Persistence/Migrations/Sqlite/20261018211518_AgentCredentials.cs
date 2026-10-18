using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    KeyHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Sites = table.Column<string>(type: "TEXT", nullable: false),
                    Types = table.Column<string>(type: "TEXT", nullable: true),
                    Fields = table.Column<string>(type: "TEXT", nullable: true),
                    Operations = table.Column<string>(type: "TEXT", nullable: false),
                    CallsPerPeriod = table.Column<int>(type: "INTEGER", nullable: false),
                    CallPeriod = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    LlmSpendPerPeriod = table.Column<decimal>(type: "TEXT", nullable: false),
                    LlmSpendPeriod = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ItemsPerChangeSet = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeSetsPerDay = table.Column<int>(type: "INTEGER", nullable: false)
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
