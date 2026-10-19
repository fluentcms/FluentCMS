using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class EntryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Entries",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Draft");

            // Entries already published before states existed are published (CNT-01).
            migrationBuilder.Sql("""UPDATE "Entries" SET "State" = 'Published' WHERE "PublishedVersion" IS NOT NULL;""");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Entries_State",
                table: "Entries",
                sql: "\"State\" IN ('Draft', 'Published', 'Reverted', 'Archived')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Entries_State",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Entries");
        }
    }
}
