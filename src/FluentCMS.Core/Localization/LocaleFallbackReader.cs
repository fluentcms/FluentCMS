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
internal sealed class LocaleFallbackReader(ContentQueryEngine engine, SchemaModel schema, LocaleRegistry registry, ContentQueryOptions options)
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

    // An entry by id that has no row in the requested locale: read from the first locale of the chain that has one, under
    // the requested locale, with what that locale and the ones after it serve named under "fallbacks".
    public async Task<ContentDocument?> ReadFromChainAsync(string typeName, ContentQuery query, Guid id, FallbackChain chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(chain);

        var fallbacks = chain.Fallbacks.ToList();
        for (var index = 0; index < fallbacks.Count; index++)
        {
            var read = await ReadAsync(typeName, query, fallbacks[index], [id], [.. query.Select], cancellationToken);
            if (read.Count == 0)
            {
                continue;
            }

            var entry = read[0];
            var served = new ContentDocument();
            if (schema.FindContentType(typeName) is { } type)
            {
                foreach (var name in type.Fields.Where(field => field.Localizable && !field.Hidden).Select(field => field.Name))
                {
                    if (entry.TryGetValue(name, out var value) && !IsMissing(value))
                    {
                        served[name] = fallbacks[index];
                    }
                }
            }

            await ApplyAsync(typeName, query, read, new FallbackChain(fallbacks.Skip(index)), cancellationToken);
            if (entry.TryGetValue(FallbacksKey, out var further) && further is ContentDocument map)
            {
                foreach (var (name, locale) in map)
                {
                    served[name] = locale;
                }
            }

            entry["locale"] = chain.Requested;
            if (served.Count > 0)
            {
                entry[FallbacksKey] = served;
            }

            return entry;
        }

        return null;
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
            foreach (var entry in await ReadAsync(typeName, query, locale, ids, select: [.. query.Select], cancellationToken))
            {
                byEntry[(Guid)entry["id"]!][locale] = entry;
            }
        }

        return byEntry;
    }

    // The filter names the ids, and counts against MaxFilterNodes, so a long list is read in batches that stay under it.
    private async Task<IReadOnlyList<ContentDocument>> ReadAsync(string typeName, ContentQuery query, string locale, IReadOnlyList<Guid> ids, List<string> select, CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, Math.Min(options.MaxPageSize, options.MaxFilterNodes - 1));
        List<ContentDocument> found = [];
        foreach (var batch in ids.Chunk(batchSize))
        {
            ContentFilter filter = batch.Length == 1
                ? new FieldFilter("id", FilterOperator.Equal, batch[0])
                : new FieldFilter("id", FilterOperator.In, batch.ToList());

            var other = query with
            {
                Filter = filter,
                Sort = [],
                Page = new PageRequest { Size = batch.Length, WithTotal = false },
                Cursor = null,
                Select = select,
                Populate = select.Count == 0 ? query.Populate : [.. query.Populate.Where(path => path == "*" || select.Contains(path.Split('.')[0]))],
                Locale = locale,
                Fallback = LocaleFallback.None,
            };

            try
            {
                found.AddRange((await engine.ExecuteAsync(typeName, other, cancellationToken)).Items);
            }
            catch (ValidationException exception) when (exception.Errors.All(RefusesTheLocale))
            {
                // The query was valid in the requested locale, so the principal may not read this one. Any other
                // refusal, such as a limit, is the caller's to see.
                return [];
            }
        }

        return found;
    }

    private static bool RefusesTheLocale(ValidationError error) =>
        (error.Path, error.Code) is ("type", QueryErrorCodes.UnknownField) or ("status", QueryErrorCodes.DraftNotAllowed);

    // A draft writes '' and the empty rich text document for what it lacks (spec 4.3), so those count as no value.
    public static bool IsMissing(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        RichText richText => richText == RichText.Empty,
        JsonArray array => array.Count == 0,
        ICollection collection => collection.Count == 0,
        _ => false,
    };
}
