using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SearchDocuments : Migration
    {
        // A GUID in hyphenated form, which SearchText leaves out.
        private static readonly string GuidPattern =
            string.Join("-", new[] { 8, 4, 4, 4, 12 }.Select(length => string.Concat(Enumerable.Repeat("[0-9a-fA-F]", length))));

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Outside the EF model: an FTS5 table (ADR-0010). Diacritics are kept, as PostgreSQL's simple configuration keeps them,
            // so both providers match the same words.
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE "SearchDocuments" USING fts5("EntryId" UNINDEXED, "Text", tokenize = 'unicode61 remove_diacritics 0');
                """);

            // Every entry that already exists, flattened as SearchText does: each string and number of its latest version but a GUID. The
            // tokenizer splits on everything but letters and digits and folds case, as SearchText does before indexing.
            migrationBuilder.Sql($"""
                INSERT INTO "SearchDocuments" ("EntryId", "Text")
                SELECT entry."Id",
                       coalesce((
                           SELECT group_concat(item.value, ' ')
                           FROM json_tree(version."Fields") AS item
                           WHERE item.type IN ('text', 'integer', 'real') AND NOT (item.type = 'text' AND item.value GLOB '{GuidPattern}')), '')
                FROM "Entries" AS entry
                JOIN "EntryVersions" AS version ON version."EntryId" = entry."Id" AND version."Version" = entry."LatestVersion";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE "SearchDocuments";""");
        }
    }
}
