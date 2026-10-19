using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Jobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InitiatorKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    InitiatorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InitiatorName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ProgressDone = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgressTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    ProgressMessage = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.CheckConstraint("CK_Jobs_Attempts", "\"Attempts\" >= 0 AND \"MaxAttempts\" >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_State_ScheduledAt",
                table: "Jobs",
                columns: new[] { "State", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_SubjectType_SubjectId",
                table: "Jobs",
                columns: new[] { "SubjectType", "SubjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
