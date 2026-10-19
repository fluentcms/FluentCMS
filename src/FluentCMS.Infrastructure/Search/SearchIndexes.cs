using FluentCMS.Core.Search;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Search;

// The SearchDocuments table is outside the EF model, because each provider stores it its own way (ADR-0010); both are written with
// SQL inside the current transaction, so an index change commits or rolls back with the write it reflects. The text is already
// lowercase words (SearchText), so both tokenizers only split it on spaces.
internal sealed class PostgreSqlSearchIndex(FluentCmsDbContext context) : ISearchIndex
{
    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "SearchDocuments" ("EntryId", "Text") VALUES ({document.EntryId}, {document.Text})
            ON CONFLICT ("EntryId") DO UPDATE SET "Text" = EXCLUDED."Text"
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<SearchMatch>> MatchAsync(IReadOnlyList<string> terms, int skip, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // Every term as a prefix; terms are letters and digits only, so nothing in them is tsquery syntax.
        var query = string.Join(" & ", terms.Select(term => term + ":*"));
        var rows = await context.Database
            .SqlQuery<MatchRow>(
                $"""
                SELECT "EntryId", ts_rank("Document", matched)::float8 AS "Rank"
                FROM "SearchDocuments", to_tsquery('simple', {query}) AS matched
                WHERE "Document" @@ matched
                ORDER BY "Rank" DESC, "EntryId"
                LIMIT {limit} OFFSET {skip}
                """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SearchMatch(row.EntryId, row.Rank))];
    }
}

internal sealed class SqliteSearchIndex(FluentCmsDbContext context) : ISearchIndex
{
    public async Task IndexAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // An FTS5 table has no unique key besides its rowid, so the entry's row is replaced.
        await context.Database.ExecuteSqlAsync($"""DELETE FROM "SearchDocuments" WHERE "EntryId" = {document.EntryId}""", cancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""INSERT INTO "SearchDocuments" ("EntryId", "Text") VALUES ({document.EntryId}, {document.Text})""",
            cancellationToken);
    }

    public async Task<IReadOnlyList<SearchMatch>> MatchAsync(IReadOnlyList<string> terms, int skip, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);

        // Every term as a quoted prefix; terms are letters and digits only, so nothing in them is FTS5 syntax.
        var query = string.Join(" AND ", terms.Select(term => $"\"{term}\"*"));
        var rows = await context.Database
            .SqlQuery<MatchRow>(
                $"""
                SELECT "EntryId", -bm25("SearchDocuments") AS "Rank"
                FROM "SearchDocuments"
                WHERE "SearchDocuments" MATCH {query}
                ORDER BY bm25("SearchDocuments"), "EntryId"
                LIMIT {limit} OFFSET {skip}
                """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SearchMatch(row.EntryId, row.Rank))];
    }
}

internal sealed record MatchRow(Guid EntryId, double Rank);
