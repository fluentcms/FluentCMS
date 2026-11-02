using System.Collections;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Localization;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Localization;

// The locale half of a read, on top of ContentQueryEngine, which reads one locale and never falls back. IContentService
// (#2693) reads in the requested locale and then calls ApplyAsync on the entries; a list filters on the requested
// locale only (decision 44), so only the fields of the entries it returned can fall back. Every read goes through
// the engine, so a fallback locale is checked and field-filtered like the requested one.
internal sealed class LocaleFallbackReader(ContentQueryEngine engine, SchemaModel schema, LocaleRegistry registry)
{
    public const string FallbacksKey = "fallbacks";

    // The chain to read: the requested locale alone for fallback=none, otherwise what the site sets for it.
    public async Task<FallbackChain> ChainAsync(SiteId site, string locale, LocaleFallback fallback, CancellationToken cancellationToken = default) =>
        fallback == LocaleFallback.None ? FallbackChain.Only(locale) : (await registry.GetAsync(site, cancellationToken)).Chain(locale);

    // Fills each localized top-level field that is missing in an entry from the first locale of the chain that has a
    // value, and names that locale under "fallbacks". An entry that falls back nowhere gets no "fallbacks" key.
    // A field the query did not select is not there to fill.
    public async Task ApplyAsync(string typeName, ContentQuery query, IReadOnlyList<ContentDocument> entries, FallbackChain chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(chain);

        if (query.Fallback == LocaleFallback.None || schema.FindContentType(typeName) is not { Localized: true } type)
        {
            return;
        }

        var localized = type.Fields.Where(field => field.Localizable && !field.Hidden).Select(field => field.Name).ToList();
        var pending = new Dictionary<Guid, (ContentDocument Entry, List<string> Fields)>();
        foreach (var entry in entries)
        {
            var missing = localized.Where(name => entry.TryGetValue(name, out var value) && IsMissing(value)).ToList();
            if (missing.Count > 0 && entry["id"] is Guid id)
            {
                pending[id] = (entry, missing);
            }
        }

        var served = new Dictionary<Guid, ContentDocument>();
        foreach (var locale in chain.Fallbacks)
        {
            if (pending.Count == 0)
            {
                break;
            }

            var wanted = pending.Values.SelectMany(item => item.Fields).Distinct().ToList();
            var read = await ReadAsync(typeName, query, locale, [.. pending.Keys], wanted, cancellationToken);
            foreach (var other in read)
            {
                var id = (Guid)other["id"]!;
                var (entry, fields) = pending[id];
                foreach (var name in fields.Where(name => other.TryGetValue(name, out var value) && !IsMissing(value)).ToList())
                {
                    entry[name] = other[name];
                    fields.Remove(name);
                    if (!served.TryGetValue(id, out var map))
                    {
                        served[id] = map = new ContentDocument();
                    }

                    map[name] = locale;
                }

                if (fields.Count == 0)
                {
                    pending.Remove(id);
                }
            }
        }

        foreach (var (id, map) in served)
        {
            entries.First(entry => entry["id"] is Guid found && found == id)[FallbacksKey] = map;
        }
    }

    // The locale=all read of entries already found: the stored values of each, by locale code, in the order the site
    // lists its locales. A locale the principal cannot read, or in which an entry has no row, is not in the map. A
    // type that is not localized has one document, under "".
    public async Task<IReadOnlyDictionary<Guid, ContentDocument>> ReadAllAsync(string typeName, ContentQuery query, IReadOnlyList<Guid> ids, SiteId site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(ids);

        var locales = schema.FindContentType(typeName) is { Localized: true } ? (await registry.GetAsync(site, cancellationToken)).Codes : [""];
        var byEntry = ids.ToDictionary(id => id, _ => new ContentDocument());
        foreach (var locale in locales)
        {
            foreach (var entry in await ReadAsync(typeName, query, locale, ids, select: [], cancellationToken))
            {
                byEntry[(Guid)entry["id"]!][locale] = entry;
            }
        }

        return byEntry;
    }

    private async Task<IReadOnlyList<ContentDocument>> ReadAsync(string typeName, ContentQuery query, string locale, IReadOnlyList<Guid> ids, List<string> select, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var other = query with
        {
            Filter = new FieldFilter("id", FilterOperator.In, ids.ToList()),
            Sort = [],
            Page = new PageRequest { Size = ids.Count, WithTotal = false },
            Cursor = null,
            Select = select,
            Populate = select.Count == 0 ? query.Populate : [.. query.Populate.Where(path => path == "*" || select.Contains(path.Split('.')[0]))],
            Locale = locale,
            Fallback = LocaleFallback.None,
        };

        try
        {
            return (await engine.ExecuteAsync(typeName, other, cancellationToken)).Items;
        }
        catch (ValidationException)
        {
            // The query was valid in the requested locale, so this is a locale that the principal may not read.
            return [];
        }
    }

    // A draft writes '' and the empty rich text document for what it lacks (spec 4.3), so those count as no value.
    private static bool IsMissing(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        RichText richText => richText == RichText.Empty,
        JsonArray array => array.Count == 0,
        ICollection collection => collection.Count == 0,
        _ => false,
    };
}
