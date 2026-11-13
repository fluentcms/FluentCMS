using FluentCMS.Content;
using FluentCMS.Localization;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Search;

// Reads what entries hold now, through the content service as the system principal of the event or the job, and writes the
// index to match. Reading the entry rather than the event makes a late, repeated or reordered delivery harmless.
internal sealed class SearchIndexer(
    IContentService content,
    IContentSurface surface,
    ILocaleResolver locales,
    SchemaModel schema,
    SearchStore store,
    TimeProvider time,
    ILogger<SearchIndexer> logger)
{
    private const int MaxBatchSize = 100;

    // The rows of the entry in the locale of an event, and in every other locale the index holds for it, since a field
    // that is not localizable is shared by the locales. A restore writes the rows again in the locales the site allows. A
    // locale the site does not allow is not read: the rows of a site's entry in it go, while those of a global entry stay
    // for the sites that allow it.
    public async Task IndexAsync(string typeName, Guid id, string locale, Guid site, CancellationToken cancellationToken)
    {
        if (schema.FindContentType(typeName) is not { } type)
        {
            await store.RemoveDocumentAsync(id, cancellationToken);
            return;
        }

        var fields = SearchText.Fields(type);
        var codes = type.Localized ? (await store.LocalesOfAsync(id, cancellationToken)).Append(locale).Distinct(StringComparer.Ordinal).ToList() : [""];
        var rows = new List<SearchRow>();
        foreach (var code in codes)
        {
            var read = (Read: true, Draft: (string?)null, Published: (string?)null);
            if (type.Localized && await AllowedAsync(new SiteId(site), code, cancellationToken) is null)
            {
                read.Read = false;
            }
            else if (fields.Count > 0)
            {
                read = await ReadAsync(type, fields, id, code, cancellationToken);
            }

            if (!read.Read && type.Scope == ContentScope.Global)
            {
                continue;
            }

            rows.Add(new SearchRow(id, code, SearchStates.Draft, type.Name, SiteOf(type, site), read.Draft));
            rows.Add(new SearchRow(id, code, SearchStates.Published, type.Name, SiteOf(type, site), read.Published));
        }

        await store.WriteAsync(rows, cancellationToken);
    }

    // A delete or a purge acts on the whole entry, so every row of it goes, in every locale, whatever the site allows. A
    // restore delivered before the delete's own event has brought the entry back, which a read in the event's locale shows.
    public async Task RemoveAsync(string typeName, Guid id, string locale, Guid site, CancellationToken cancellationToken)
    {
        if (schema.FindContentType(typeName) is { } type && await ExistsAsync(type, id, locale, site, cancellationToken))
        {
            await IndexAsync(typeName, id, locale, site, cancellationToken);
            return;
        }

        await store.RemoveDocumentAsync(id, cancellationToken);
    }

    // Every entry of the type, or of every type with a searchable field, in the site and of the global types: the drafts
    // and the published versions, a page at a time, in each locale the site allows. Then the rows that no entry had are
    // removed: of a site's type every such row, a row in a locale the site no longer allows included, and of a global type
    // only those in the locales the site allows, since another site may allow the others. A second run writes nothing.
    public async Task<(int Written, int Removed)> RebuildAsync(string? typeName, SiteId site, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        var searchable = schema.ContentTypes.Where(type => SearchText.Fields(type).Count > 0).ToList();
        var types = typeName is null ? searchable : searchable.Where(type => type.Name == typeName).ToList();

        var written = 0;
        var removed = 0;
        foreach (var type in types)
        {
            var fields = SearchText.Fields(type);
            var seen = new HashSet<(Guid DocumentId, string Locale, int State)>();
            var allowed = await LocalesAsync(type, site, cancellationToken);
            foreach (var locale in allowed)
            {
                written += await IndexListAsync(type, fields, locale, QueryStatus.Draft, site, seen, cancellationToken);
                written += await IndexListAsync(type, fields, locale, QueryStatus.Published, site, seen, cancellationToken);
            }

            var swept = type.Localized && type.Scope == ContentScope.Global ? allowed.Select(locale => locale!).ToList() : null;
            removed += await store.SweepAsync(type.Name, SiteOf(type, site.Value), swept, seen, started, cancellationToken);
        }

        if (typeName is null)
        {
            removed += await store.RemoveOtherTypesAsync([.. searchable.Select(type => type.Name)], site.Value, cancellationToken);
        }

        return (written, removed);
    }

    private static Guid? SiteOf(SchemaType type, Guid site) => type.Scope == ContentScope.Global ? null : site;

    // The code as the site allows it, or null for a locale the site does not allow.
    private async Task<string?> AllowedAsync(SiteId site, string code, CancellationToken cancellationToken)
    {
        try
        {
            return await locales.ResolveAsync(new LocaleRequest(site) { Parameter = code }, cancellationToken);
        }
        catch (ValidationException)
        {
            return null;
        }
    }

    // Whether the entry is out of the trash in that locale. A locale the site does not allow cannot tell, and counts as gone.
    private async Task<bool> ExistsAsync(SchemaType type, Guid id, string locale, Guid site, CancellationToken cancellationToken)
    {
        if (type.Localized && await AllowedAsync(new SiteId(site), locale, cancellationToken) is null)
        {
            return false;
        }

        try
        {
            var options = new GetOptions { Status = QueryStatus.Draft, Locale = type.Localized ? locale : null, Fallback = LocaleFallback.None, Select = ["id"] };
            return await content.GetAsync(type.Name, id, options, cancellationToken) is not null;
        }
        catch (ValidationException)
        {
            return false;
        }
    }

    private async Task<(bool Read, string? Draft, string? Published)> ReadAsync(SchemaType type, IReadOnlyList<SchemaField> fields, Guid id, string locale, CancellationToken cancellationToken)
    {
        var options = new GetOptions { Locale = type.Localized ? locale : null, Fallback = LocaleFallback.None, Select = [.. fields.Select(field => field.Name)] };
        try
        {
            var draft = await content.GetAsync(type.Name, id, options with { Status = QueryStatus.Draft }, cancellationToken);
            var published = await content.GetAsync(type.Name, id, options with { Status = QueryStatus.Published }, cancellationToken);
            return (true, draft is null ? null : SearchText.Of(draft, fields), published is null ? null : SearchText.Of(published, fields));
        }
        catch (ValidationException exception)
        {
            SearchLog.NotRead(logger, type.Name, id, locale, exception.Errors[0].Code);
            return (false, null, null);
        }
    }

    // The locales the site allows among those a localized type is found in: the site's default, those of the entries that
    // have it, and those the index already holds. A list reads one locale, and the SDK has no list of a site's locales.
    private async Task<IReadOnlyList<string?>> LocalesAsync(SchemaType type, SiteId site, CancellationToken cancellationToken)
    {
        if (!type.Localized)
        {
            return [null];
        }

        var codes = new SortedSet<string>(StringComparer.Ordinal)
        {
            await locales.ResolveAsync(new LocaleRequest(site), cancellationToken),
        };
        codes.UnionWith(await store.LocalesOfAsync(type.Name, SiteOf(type, site.Value), cancellationToken));

        string? cursor = null;
        do
        {
            var page = await content.QueryAsync(type.Name, Query(ILocaleResolver.All, QueryStatus.Draft, ["id"], cursor), cancellationToken);
            foreach (var item in page.Items)
            {
                if (item.TryGetValue("locales", out var map) && map is IEnumerable<KeyValuePair<string, object?>> byLocale)
                {
                    codes.UnionWith(byLocale.Select(pair => pair.Key));
                }
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var allowed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            if (await AllowedAsync(site, code, cancellationToken) is { } canonical)
            {
                allowed.Add(canonical);
            }
        }

        return [.. allowed];
    }

    private async Task<int> IndexListAsync(SchemaType type, IReadOnlyList<SchemaField> fields, string? locale, QueryStatus status, SiteId site, HashSet<(Guid, string, int)> seen, CancellationToken cancellationToken)
    {
        var state = status == QueryStatus.Draft ? SearchStates.Draft : SearchStates.Published;
        var written = 0;
        string? cursor = null;
        do
        {
            var page = await content.QueryAsync(type.Name, Query(locale, status, ["id", .. fields.Select(field => field.Name)], cursor), cancellationToken);
            var rows = page.Items.Select(item => new SearchRow((Guid)item["id"]!, locale ?? "", state, type.Name, SiteOf(type, site.Value), SearchText.Of(item, fields))).ToList();
            foreach (var row in rows)
            {
                seen.Add((row.DocumentId, row.Locale, row.State));
            }

            written += await store.WriteAsync(rows, cancellationToken);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return written;
    }

    private ContentQuery Query(string? locale, QueryStatus status, IReadOnlyList<string> select, string? cursor) => new()
    {
        Status = status,
        Locale = locale,
        Fallback = LocaleFallback.None,
        Select = select,
        Page = new PageRequest { Size = Math.Min(MaxBatchSize, surface.MaxPageSize), WithTotal = false },
        Cursor = cursor,
    };
}
