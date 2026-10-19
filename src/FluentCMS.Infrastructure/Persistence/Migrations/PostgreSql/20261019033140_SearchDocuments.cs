using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FluentCMS.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class SearchDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Outside the EF model: the tsvector is generated from the text, under the simple configuration so no language's
            // stemming applies, and searched through a GIN index (ADR-0010).
            migrationBuilder.Sql("""
                CREATE TABLE "SearchDocuments" (
                    "EntryId" uuid NOT NULL PRIMARY KEY,
                    "Text" text NOT NULL,
                    "Document" tsvector GENERATED ALWAYS AS (to_tsvector('simple', "Text")) STORED
                );
                CREATE INDEX "IX_SearchDocuments_Document" ON "SearchDocuments" USING GIN ("Document");
                """);

            // Every entry that already exists, flattened as SearchText does: each string and number of its latest version, as
            // lowercase words, cut at SearchText.MaxLength.
            migrationBuilder.Sql("""
                INSERT INTO "SearchDocuments" ("EntryId", "Text")
                SELECT entry."Id",
                       left(trim(regexp_replace(lower(coalesce((
                           SELECT string_agg(item #>> '{}', ' ')
                           FROM jsonb_path_query(version."Fields", 'strict $.**') AS item
                           WHERE jsonb_typeof(item) IN ('string', 'number')), '')), '[^[:alnum:]]+', ' ', 'g')), 100000)
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
