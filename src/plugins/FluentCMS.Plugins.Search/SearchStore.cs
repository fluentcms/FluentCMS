using FluentCMS.Content;
using FluentCMS.Providers;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Search;

// What the index holds of a document in a locale and a state: the folded text of each searchable field, or null to remove
// its rows.
internal sealed record SearchRow(Guid DocumentId, string Locale, int State, string Type, Guid? SiteId, IReadOnlyDictionary<string, string>? Texts);

// The plugin's own table, and the SQL of each provider that matches it: an FTS5 table on SQLite and a GIN index over
// to_tsvector on PostgreSQL, which the migration of each set adds, and LIKE on SQL Server, which has no full-text catalogue
// here. The words are SearchText's, letters and digits only, so they need no escaping in a match expression.
internal sealed class SearchStore(PluginDbContext<SearchPlugin> database, TimeProvider time)
{
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int DeleteBatch = 500;

    // Every word matches one of the fields, not always the same one, the last word as the start of a word, or on SQL Server
    // every word as a part of the text. The most recently indexed come first when more match than the limit, as the seam
    // has no ranking; only the rows of those fields count, so another field decides nothing.
    public async Task<IReadOnlyList<Guid>> MatchAsync(SearchRequest request, IReadOnlyList<string> words, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        var state = request.Status == QueryStatus.Draft ? SearchStates.Draft : SearchStates.Published;
        var site = request.Site?.Value;
        var names = fields.ToList();
        IQueryable<SearchEntry> Of(IQueryable<SearchEntry> rows)
        {
            rows = rows.Where(entry => entry.Type == request.Type && entry.Locale == request.Locale && entry.State == state);
            if (site is not null)
            {
                rows = rows.Where(entry => entry.SiteId == site);
            }

            return rows.Where(entry => names.Contains(entry.Field));
        }

        var hits = Of(database.Set<SearchEntry>().AsNoTracking());
        for (var index = 0; index < words.Count; index++)
        {
            var matched = Of(Matching(words[index], prefix: index == words.Count - 1)).Select(entry => entry.DocumentId);
            hits = hits.Where(entry => matched.Contains(entry.DocumentId));
        }

        return await hits.GroupBy(entry => entry.DocumentId)
            .Select(group => new { DocumentId = group.Key, UpdatedAt = group.Max(entry => entry.UpdatedAt) })
            .OrderByDescending(hit => hit.UpdatedAt).ThenBy(hit => hit.DocumentId)
            .Select(hit => hit.DocumentId)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> LocalesOfAsync(Guid documentId, CancellationToken cancellationToken) =>
        await database.Set<SearchEntry>().Where(entry => entry.DocumentId == documentId).Select(entry => entry.Locale).Distinct().ToListAsync(cancellationToken);

    public async Task<List<string>> LocalesOfAsync(string type, Guid? site, CancellationToken cancellationToken) =>
        await database.Set<SearchEntry>().Where(entry => entry.Type == type && entry.SiteId == site).Select(entry => entry.Locale).Distinct().ToListAsync(cancellationToken);

    // Inserts, changes and removes rows to match, a row per field, and leaves a row whose text is already the same as it
    // is. The rows of a field that is not given go. Returns how many rows changed. A write that adds rows of a site holds
    // the site to its commit, so that the site's deletion waits for it and its rows go with DeletedSiteSearch, or it finds
    // the site gone and is refused with FCMS2301 (#3586); the outbox tries the delivery again, and the entry, which went
    // before its site, is then read as gone and adds no row.
    public async Task<int> WriteAsync(IReadOnlyCollection<SearchRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return 0;
        }

        var ids = rows.Select(row => row.DocumentId).Distinct().ToList();
        var existing = (await database.Set<SearchEntry>().Where(entry => ids.Contains(entry.DocumentId)).ToListAsync(cancellationToken))
            .GroupBy(entry => (entry.DocumentId, entry.Locale, entry.State))
            .ToDictionary(group => group.Key, group => group.ToDictionary(entry => entry.Field, StringComparer.Ordinal));

        var now = time.GetUtcNow();
        var changed = 0;
        foreach (var row in rows)
        {
            var key = (row.DocumentId, row.Locale, row.State);
            if (!existing.TryGetValue(key, out var fields))
            {
                existing[key] = fields = new(StringComparer.Ordinal);
            }

            foreach (var (field, entry) in fields.Where(pair => row.Texts?.ContainsKey(pair.Key) != true).ToList())
            {
                database.Remove(entry);
                fields.Remove(field);
                changed++;
            }

            foreach (var (field, text) in row.Texts ?? new Dictionary<string, string>())
            {
                if (!fields.TryGetValue(field, out var entry))
                {
                    entry = new SearchEntry { DocumentId = row.DocumentId, Locale = row.Locale, State = row.State, Type = row.Type, SiteId = row.SiteId, Field = field, Text = text, UpdatedAt = now };
                    database.Add(entry);
                    fields[field] = entry;
                    changed++;
                }
                else if (entry.Text != text || entry.Type != row.Type || entry.SiteId != row.SiteId)
                {
                    entry.Text = text;
                    entry.Type = row.Type;
                    entry.SiteId = row.SiteId;
                    entry.UpdatedAt = now;
                    changed++;
                }
            }
        }

        if (changed > 0)
        {
            await SaveAsync(cancellationToken);
        }

        database.ChangeTracker.Clear();
        return changed;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var sites = database.ChangeTracker.Entries<SearchEntry>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.SiteId is not null)
            .Select(entry => entry.Entity.SiteId!.Value)
            .Distinct()
            .Order()
            .ToList();
        if (sites.Count == 0)
        {
            await database.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        foreach (var site in sites)
        {
            await database.HoldSiteAsync(new SiteId(site), cancellationToken);
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(CancellationToken.None);
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

    // The rows whose text holds the word, or a word that starts with it.
    private IQueryable<SearchEntry> Matching(string word, bool prefix)
    {
        switch (database.Database.ProviderName)
        {
            case SqliteProvider:
                var match = prefix ? $"\"{word}\"*" : $"\"{word}\"";
                return database.Set<SearchEntry>().FromSql(
                    $"""SELECT * FROM cms_search_entry WHERE id IN (SELECT rowid FROM cms_search_entry_fts WHERE cms_search_entry_fts MATCH {match})""");
            case PostgreSqlProvider:
                var query = prefix ? $"'{word}':*" : $"'{word}'";
                return database.Set<SearchEntry>().FromSql(
                    $"""SELECT * FROM cms_search_entry WHERE to_tsvector('simple'::regconfig, "text") @@ to_tsquery('simple'::regconfig, {query})""");
            default:
                var pattern = $"%{word}%";
                return database.Set<SearchEntry>().Where(entry => EF.Functions.Like(entry.Text, pattern));
        }
    }
}
