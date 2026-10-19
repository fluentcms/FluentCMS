using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class RoutingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoutingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ActiveVersion = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRules", x => x.Id);
                    table.CheckConstraint("CK_RoutingRules_ActiveVersion", "\"ActiveVersion\" IS NULL OR (\"ActiveVersion\" >= 1 AND \"ActiveVersion\" <= \"Version\")");
                    table.CheckConstraint("CK_RoutingRules_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_RoutingRules_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoutingRuleVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoutingRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Definition = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingRuleVersions", x => x.Id);
                    table.CheckConstraint("CK_RoutingRuleVersions_Version", "\"Version\" >= 1");
                    table.ForeignKey(
                        name: "FK_RoutingRuleVersions_RoutingRules_RoutingRuleId",
                        column: x => x.RoutingRuleId,
                        principalTable: "RoutingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRules_SiteId_Name",
                table: "RoutingRules",
                columns: new[] { "SiteId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutingRuleVersions_RoutingRuleId_Version",
                table: "RoutingRuleVersions",
                columns: new[] { "RoutingRuleId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoutingRuleVersions");

            migrationBuilder.DropTable(
                name: "RoutingRules");
        }
    }
}
