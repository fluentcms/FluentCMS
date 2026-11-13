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
    // that is not localizable is shared by the locales. The draft of an entry in the trash stays, so that the trash can be
    // searched; the engine keeps it out of every other list.
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
            var (draft, published) = fields.Count == 0 ? (null, null) : await ReadAsync(type, fields, id, code, cancellationToken);
            rows.Add(new SearchRow(id, code, SearchStates.Draft, type.Name, SiteOf(type, site), draft));
            rows.Add(new SearchRow(id, code, SearchStates.Published, type.Name, SiteOf(type, site), published));
        }

        await store.WriteAsync(rows, cancellationToken);
    }

    // Every entry of the type, or of every type with a searchable field, in the site and of the global types: the drafts,
    // the published versions and the drafts in the trash, a page at a time. Then the rows that no entry had are removed.
    // A second run writes nothing.
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
            foreach (var locale in await LocalesAsync(type, site, cancellationToken))
            {
                written += await IndexListAsync(type, fields, locale, QueryStatus.Draft, trash: false, site, seen, cancellationToken);
                written += await IndexListAsync(type, fields, locale, QueryStatus.Published, trash: false, site, seen, cancellationToken);
                written += await IndexListAsync(type, fields, locale, QueryStatus.Draft, trash: true, site, seen, cancellationToken);
            }

            removed += await store.SweepAsync(type.Name, SiteOf(type, site.Value), seen, started, cancellationToken);
        }

        if (typeName is null)
        {
            removed += await store.RemoveOtherTypesAsync([.. searchable.Select(type => type.Name)], site.Value, cancellationToken);
        }

        return (written, removed);
    }

    private static Guid? SiteOf(SchemaType type, Guid site) => type.Scope == ContentScope.Global ? null : site;

    private async Task<(string? Draft, string? Published)> ReadAsync(SchemaType type, IReadOnlyList<SchemaField> fields, Guid id, string locale, CancellationToken cancellationToken)
    {
        var options = new GetOptions { Locale = type.Localized ? locale : null, Fallback = LocaleFallback.None, Select = [.. fields.Select(field => field.Name)] };
        try
        {
            var draft = await content.GetAsync(type.Name, id, options with { Status = QueryStatus.Draft }, cancellationToken)
                ?? await TrashedAsync(type, id, options, cancellationToken);
            var published = await content.GetAsync(type.Name, id, options with { Status = QueryStatus.Published }, cancellationToken);
            return (draft is null ? null : SearchText.Of(draft, fields), published is null ? null : SearchText.Of(published, fields));
        }
        catch (ValidationException exception)
        {
            // Such as a locale that the site no longer has.
            SearchLog.NotRead(logger, type.Name, id, locale, exception.Errors[0].Code);
            return (null, null);
        }
    }

    private async Task<ContentDocument?> TrashedAsync(SchemaType type, Guid id, GetOptions options, CancellationToken cancellationToken)
    {
        var query = new ContentQuery
        {
            Filter = new FieldFilter("id", FilterOperator.Equal, id),
            Locale = options.Locale,
            Fallback = LocaleFallback.None,
            Select = options.Select,
            Page = new PageRequest { Size = 1, WithTotal = false },
        };
        var items = (await content.QueryTrashAsync(type.Name, query, cancellationToken)).Items;
        return items.Count > 0 ? items[0] : null;
    }

    // The locales to list a localized type in: the site's default, those of the entries that have it, and those the index
    // already holds. A list reads one locale, and the SDK has no list of a site's locales.
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

        return [.. codes];
    }

    private async Task<int> IndexListAsync(SchemaType type, IReadOnlyList<SchemaField> fields, string? locale, QueryStatus status, bool trash, SiteId site, HashSet<(Guid, string, int)> seen, CancellationToken cancellationToken)
    {
        var state = status == QueryStatus.Draft ? SearchStates.Draft : SearchStates.Published;
        var written = 0;
        string? cursor = null;
        do
        {
            var query = Query(locale, status, ["id", .. fields.Select(field => field.Name)], cursor);
            var page = trash ? await content.QueryTrashAsync(type.Name, query, cancellationToken) : await content.QueryAsync(type.Name, query, cancellationToken);
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
