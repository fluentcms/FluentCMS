using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class ContentTypePublishedVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                table: "ContentTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ContentTypes_PublishedVersion",
                table: "ContentTypes",
                sql: "\"PublishedVersion\" IS NULL OR \"PublishedVersion\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ContentTypes_PublishedVersion",
                table: "ContentTypes");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "ContentTypes");
        }
    }
}
