using FluentCMS.Content;
using FluentCMS.Providers;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Search;

// One row of the index to write: the folded text of a document in a locale and a state, or null to remove the row.
internal sealed record SearchRow(Guid DocumentId, string Locale, int State, string Type, Guid? SiteId, string? Text);

// The plugin's own table, and the SQL of each provider that matches it: an FTS5 table on SQLite and a GIN index over
// to_tsvector on PostgreSQL, which the migration of each set adds, and LIKE on SQL Server, which has no full-text catalogue
// here. The words are SearchText's, letters and digits only, so they need no escaping in a match expression.
internal sealed class SearchStore(PluginDbContext<SearchPlugin> database, TimeProvider time)
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int DeleteBatch = 500;

    // Every word matches, the last as the start of a word, or on SQL Server every word as a part of the text. The most
    // recently indexed come first when more match than the limit, as the seam has no ranking.
    public async Task<IReadOnlyList<Guid>> MatchAsync(SearchRequest request, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var state = request.Status == QueryStatus.Draft ? SearchStates.Draft : SearchStates.Published;
        var rows = Matching(words).AsNoTracking().Where(entry => entry.Type == request.Type && entry.Locale == request.Locale && entry.State == state);
        if (request.Site is { } site)
        {
            var id = site.Value;
            rows = rows.Where(entry => entry.SiteId == id);
        }

        return await rows.OrderByDescending(entry => entry.UpdatedAt).ThenBy(entry => entry.Id)
            .Select(entry => entry.DocumentId)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> LocalesOfAsync(Guid documentId, CancellationToken cancellationToken) =>
        await database.Set<SearchEntry>().Where(entry => entry.DocumentId == documentId).Select(entry => entry.Locale).Distinct().ToListAsync(cancellationToken);

    public async Task<List<string>> LocalesOfAsync(string type, Guid? site, CancellationToken cancellationToken) =>
        await database.Set<SearchEntry>().Where(entry => entry.Type == type && entry.SiteId == site).Select(entry => entry.Locale).Distinct().ToListAsync(cancellationToken);

    // Inserts, changes and removes rows to match, and leaves a row whose text is already the same as it is. Returns how
    // many rows changed.
    public async Task<int> WriteAsync(IReadOnlyCollection<SearchRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var ids = rows.Select(row => row.DocumentId).Distinct().ToList();
        var existing = (await database.Set<SearchEntry>().Where(entry => ids.Contains(entry.DocumentId)).ToListAsync(cancellationToken))
            .ToDictionary(entry => (entry.DocumentId, entry.Locale, entry.State));

        var now = time.GetUtcNow();
        var changed = 0;
        foreach (var row in rows)
        {
            existing.TryGetValue((row.DocumentId, row.Locale, row.State), out var entry);
            if (row.Text is null)
            {
                if (entry is not null)
                {
                    database.Remove(entry);
                    existing.Remove((row.DocumentId, row.Locale, row.State));
                    changed++;
                }
            }
            else if (entry is null)
            {
                entry = new SearchEntry { DocumentId = row.DocumentId, Locale = row.Locale, State = row.State, Type = row.Type, SiteId = row.SiteId, Text = row.Text, UpdatedAt = now };
                database.Add(entry);
                existing[(row.DocumentId, row.Locale, row.State)] = entry;
                changed++;
            }
            else if (entry.Text != row.Text || entry.Type != row.Type || entry.SiteId != row.SiteId)
            {
                entry.Text = row.Text;
                entry.Type = row.Type;
                entry.SiteId = row.SiteId;
                entry.UpdatedAt = now;
                changed++;
            }
        }

        if (changed > 0)
        {
            await database.SaveChangesAsync(cancellationToken);
        }

        database.ChangeTracker.Clear();
        return changed;
    }

    public async Task<int> RemoveDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        await database.Set<SearchEntry>().Where(entry => entry.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);

    public async Task<int> RemoveSiteAsync(Guid site, CancellationToken cancellationToken) =>
        await database.Set<SearchEntry>().Where(entry => entry.SiteId == site).ExecuteDeleteAsync(cancellationToken);

    // The rows of the type in the site, or of the global type, that a rebuild did not see and that nothing wrote since it
    // started, which are of entries that no longer exist. With locales, only the rows in those. Read by key in batches, so
    // that a large index is not held at once.
    public async Task<int> SweepAsync(string type, Guid? site, IReadOnlyCollection<string>? locales, IReadOnlySet<(Guid DocumentId, string Locale, int State)> seen, DateTimeOffset started, CancellationToken cancellationToken)
    {
        var removed = 0;
        var after = 0L;
        var only = locales?.ToList();
        while (true)
        {
            var rows = database.Set<SearchEntry>().AsNoTracking().Where(entry => entry.Type == type && entry.SiteId == site && entry.Id > after);
            if (only is not null)
            {
                rows = rows.Where(entry => only.Contains(entry.Locale));
            }

            var batch = await rows
                .OrderBy(entry => entry.Id)
                .Select(entry => new { entry.Id, entry.DocumentId, entry.Locale, entry.State, entry.UpdatedAt })
                .Take(DeleteBatch)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return removed;
            }

            after = batch[^1].Id;
            var stale = batch.Where(entry => entry.UpdatedAt < started && !seen.Contains((entry.DocumentId, entry.Locale, entry.State))).Select(entry => entry.Id).ToList();
            if (stale.Count > 0)
            {
                removed += await database.Set<SearchEntry>().Where(entry => stale.Contains(entry.Id) && entry.UpdatedAt < started).ExecuteDeleteAsync(cancellationToken);
            }
        }
    }

    // The rows in the site, and the global ones, of the types that are not searched any more.
    public async Task<int> RemoveOtherTypesAsync(IReadOnlyCollection<string> types, Guid site, CancellationToken cancellationToken)
    {
        var kept = types.ToList();
        return await database.Set<SearchEntry>()
            .Where(entry => (entry.SiteId == site || entry.SiteId == null) && !kept.Contains(entry.Type))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<SearchEntry> Matching(IReadOnlyList<string> words)
    {
        var last = words.Count - 1;
        switch (database.Database.ProviderName)
        {
            case SqliteProvider:
                var match = string.Join(' ', words.Select((word, index) => index == last ? $"\"{word}\"*" : $"\"{word}\""));
                return database.Set<SearchEntry>().FromSql(
                    $"""SELECT * FROM cms_search_entry WHERE id IN (SELECT rowid FROM cms_search_entry_fts WHERE cms_search_entry_fts MATCH {match})""");
            case PostgreSqlProvider:
                var query = string.Join(" & ", words.Select((word, index) => index == last ? $"'{word}':*" : $"'{word}'"));
                return database.Set<SearchEntry>().FromSql(
                    $"""SELECT * FROM cms_search_entry WHERE to_tsvector('simple'::regconfig, "text") @@ to_tsquery('simple'::regconfig, {query})""");
            default:
                var rows = database.Set<SearchEntry>().AsQueryable();
                foreach (var word in words)
                {
                    var pattern = $"%{word}%";
                    rows = rows.Where(entry => EF.Functions.Like(entry.Text, pattern));
                }

                return rows;
        }
    }
}
