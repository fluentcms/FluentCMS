using System.Diagnostics;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Telemetry;
using FluentCMS.Providers;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content.Query;

// Runs a ContentQuery. IContentService (#2693) is its caller, and resolves the locale and site first. Bad input is a
// ValidationException, before anything runs. RichTextFormat and Fallback are carried, not applied: this returns the
// stored rich text and the requested locale's values. LocaleFallbackReader fills the fields that fall back.
internal sealed class ContentQueryEngine(CmsDbContext context, SchemaModel schema, IJsonSqlDialect dialect, IContentQueryAccess access, ContentQueryOptions options, ISearchIndex search, ReferenceResolvers resolvers, ManagementShape shape)
{
    private const string All = "*";
    private const string EveryLocale = "all";
    private const int MaxSearchLength = 256;

    internal sealed record CompiledQuery(
        QueryScope Scope,
        SchemaType Type,
        IQueryable<QueryRow> Filtered,
        IQueryable<object?[]> Rows,
        EntryPlan Plan,
        IReadOnlyList<SortKey> Keys,
        int[] KeyColumns,
        int PageSize,
        int? PageNumber,
        bool WithTotal,
        SearchHits? Hits);

    // The ids the index found, which the compiled query reads as a parameter when it runs.
    internal sealed class SearchHits(SearchRequest request)
    {
        public SearchRequest Request { get; } = request;

        public Guid[] Ids { get; set; } = [];
    }

    public Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync(typeName, query, everySystemField: false, cancellationToken);

    // everySystemField is for a content class, which has a property for each of them; a document leaves out the two that
    // are not in the response shape. Field permissions still decide.
    public Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, bool everySystemField, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query, everySystemField, trashed: false, cancellationToken);

    // A read by id of an entry the caller has already read or written some of. The query is compiled and checked as any other.
    public Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, bool everySystemField, KnownEntry? known, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query, everySystemField, trashed: false, cancellationToken, known: known);

    // Reads the entries in the trash as drafts. Whoever calls this has decided that the principal may look at the trash;
    // what a draft read is limited by still holds.
    public Task<ContentQueryResult> ExecuteTrashAsync(string typeName, ContentQuery query, bool everySystemField, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query with { Status = QueryStatus.Draft }, everySystemField, trashed: true, cancellationToken);

    // Reads the entries the principal may read that another row rule also lets through, such as that of a write about to
    // be made to them. The rule is trusted like the read rule, and holds for the entries read, not for relation targets.
    public Task<ContentQueryResult> ExecuteWithRuleAsync(string typeName, ContentQuery query, ContentFilter? rule, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query, everySystemField: false, trashed: false, cancellationToken, rule);

    // The number of entries a list would have, with the filter, the row rule and the site of a list, and no page or reference read.
    public async Task<long> CountAsync(string typeName, ContentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var compiled = Compile(await access.ForQueryAsync(query.Site, query.Locale ?? "", cancellationToken), typeName, query with { Page = null, Cursor = null });
        return await TimedAsync(compiled, async () =>
        {
            await SearchAsync(compiled, cancellationToken);
            return await compiled.Filtered.LongCountAsync(cancellationToken);
        });
    }

    private async Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, bool everySystemField, bool trashed, CancellationToken cancellationToken, ContentFilter? rule = null, KnownEntry? known = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        var compiled = Compile(await access.ForQueryAsync(query.Site, query.Locale ?? "", cancellationToken), typeName, query, everySystemField, trashed, rule);
        return await TimedAsync(compiled, async () =>
        {
            await SearchAsync(compiled, cancellationToken);
            return await RunAsync(compiled, cancellationToken, known);
        });
    }

    // Asked once the query is known to be valid, so that a refused query costs no search.
    private async Task SearchAsync(CompiledQuery compiled, CancellationToken cancellationToken)
    {
        if (compiled.Hits is { } hits)
        {
            var ids = await search.SearchAsync(hits.Request, cancellationToken);
            hits.Ids = [.. ids.Take(hits.Request.Limit)];
        }
    }

    // Timed from here, so that a query refused as invalid is not recorded, and tagged with the type the schema resolved.
    private static async Task<T> TimedAsync<T>(CompiledQuery compiled, Func<Task<T>> run)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await run();
        }
        finally
        {
            FluentCmsTelemetry.QueryDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Type, compiled.Type.Name));
        }
    }

    private async Task<ContentQueryResult> RunAsync(CompiledQuery compiled, CancellationToken cancellationToken, KnownEntry? known = null)
    {
        long? total = compiled.WithTotal ? await compiled.Filtered.LongCountAsync(cancellationToken) : null;
        var rows = KnownRow(compiled, known) is { } row ? [row] : await compiled.Rows.ToListAsync(cancellationToken);

        var hasMore = rows.Count > compiled.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var entries = rows.Select(row => Materialize(compiled.Plan, row, new ContentDocument(), parent: null)).ToList();
        var readable = known?.Readable ?? new ReadableTargets();
        var resolvable = new ResolvableReferences(schema, resolvers);
        await FillReferencesAsync(compiled.Scope, compiled.Plan, entries, readable, resolvable, cancellationToken, known?.References);
        await FilterNestedTargetsAsync(compiled.Scope, compiled.Type, entries, readable, cancellationToken);
        await resolvable.ResolveAsync(compiled.Scope.Locale, cancellationToken);

        var last = hasMore ? rows[^1] : null;
        var nextCursor = last is null ? null : SortKeys.Encode(compiled.Keys, compiled.KeyColumns.Select(column => last[column]).ToList());

        return new(entries.Select(entry => entry.Document).ToList(), total, compiled.PageNumber, compiled.PageSize, nextCursor);
    }

    // The query reads one entry by id, which is the caller's to match with the row, and has nothing else to filter it by.
    private static object?[]? KnownRow(CompiledQuery compiled, KnownEntry? known) =>
        known?.Row is { } row && compiled.Hits is null && !compiled.WithTotal && !compiled.Scope.ReachesTrash && compiled.Scope.Access.RowRule(compiled.Type) is null
            ? InMemoryProjection.Run(compiled.Plan.Projection(), row)
            : null;

    // Separate from running it, so that a test can read the SQL.
    internal CompiledQuery Compile(string typeName, ContentQuery query) => Compile(access, typeName, query);

    private CompiledQuery Compile(IContentQueryAccess access, string typeName, ContentQuery query, bool everySystemField = false, bool trashed = false, ContentFilter? rule = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = query.Status == QueryStatus.Draft ? ContentState.Draft : ContentState.Published;
        var scope = new QueryScope(context, schema, dialect, access, options, state, query.Locale ?? "", query.Site, trashed, shape.Applies);

        if (schema.FindContentType(typeName) is not { } type || !access.CanReadType(type))
        {
            throw new ValidationException([new ValidationError("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type that can be read.")]);
        }

        if (query.Locale == EveryLocale || (type.Localized && string.IsNullOrEmpty(query.Locale)))
        {
            scope.Report("locale", QueryErrorCodes.InvalidParameter, $"A list of {typeName} needs one locale.");
        }

        if (state == ContentState.Draft && !access.CanReadDraft(type))
        {
            scope.Report("status", QueryErrorCodes.DraftNotAllowed, $"Reading drafts of {typeName} is not allowed.");
        }

        var row = Expression.Parameter(typeof(QueryRow), "row");
        var rows = scope.Rows(type, trashedRoot: trashed);
        if (trashed)
        {
            rows = rows.IgnoreQueryFilters();
        }

        if (new FilterCompiler(scope).Where(type, query.Filter, row) is { } filter)
        {
            rows = rows.Where(Lambda(filter, row));
        }

        if (rule is not null)
        {
            rows = rows.Where(Lambda(new FilterCompiler(scope).Rule(type, rule, row), row));
        }

        var hits = Search(scope, type, query);
        if (hits is not null)
        {
            var ids = Expression.Property(Expression.Constant(hits), nameof(SearchHits.Ids));
            rows = rows.Where(Lambda(Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)], ids, QueryExpressions.DocumentId(row)), row));
        }

        var select = everySystemField && query.Select.Count == 0 ? SelectNode.EverythingWithSystem : Selection(scope, type, query.Select);
        var plan = new EntryPlan(scope, type, select, Population(scope, type, query), level: 0, isRoot: true, row);
        var keys = SortKeys.Build(scope, type, query.Sort, row);
        var keyColumns = keys.Select(key => plan.AddColumn(key.Value)).ToArray();
        var (skip, size, number, withTotal) = Page(scope, query);

        var ordered = SortKeys.Apply(rows, keys, row);
        if (query.Cursor is { } cursor)
        {
            if (SortKeys.Decode(cursor, keys) is { } values)
            {
                ordered = SortKeys.Apply(rows.Where(Lambda(SortKeys.After(keys, values), row)), keys, row);
            }
            else
            {
                scope.Report("cursor", QueryErrorCodes.InvalidCursor, "The cursor is not valid for this query.");
            }
        }

        scope.ThrowIfInvalid();

        // One row more than the page tells whether another page follows, without a count.
        var page = skip > 0 ? ordered.Skip(skip).Take(size + 1) : ordered.Take(size + 1);
        return new(scope, type, rows, page.Select(plan.Projection()), plan, keys, keyColumns, size, number, withTotal, hits);
    }

    // The index finds the entries; the query keeps its filter, row rule, site, locale, status and sort.
    private SearchHits? Search(QueryScope scope, SchemaType type, ContentQuery query)
    {
        if (query.Search is not { } text)
        {
            return null;
        }

        if (search is NoSearchIndex)
        {
            scope.Report("q", QueryErrorCodes.NoSearchIndex, "Searching needs a search index, and none is registered. Add the search plugin or fill the ISearchIndex provider slot.");
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            scope.Report("q", QueryErrorCodes.InvalidValue, "A search needs a text to search for.");
        }
        else if (text.Length > MaxSearchLength)
        {
            scope.Report("q", QueryErrorCodes.LimitExceeded, $"A search text has at most {MaxSearchLength} characters.");
        }
        else
        {
            return new SearchHits(new SearchRequest(type.Name, text, options.MaxSearchResults)
            {
                Locale = type.Localized ? scope.Locale : "",
                Site = type.Scope == ContentScope.Site ? scope.Site : null,
                Status = query.Status ?? QueryStatus.Published,
            });
        }

        return null;
    }

    private static Expression<Func<QueryRow, bool>> Lambda(Expression body, ParameterExpression row) => Expression.Lambda<Func<QueryRow, bool>>(body, row);

    private (int Skip, int Size, int? Number, bool WithTotal) Page(QueryScope scope, ContentQuery query)
    {
        var page = query.Page ?? new PageRequest();
        var size = page.Size ?? options.DefaultPageSize;

        if (size < 1 || size > options.MaxPageSize)
        {
            scope.Report("pageSize", QueryErrorCodes.LimitExceeded, $"The page size must be between 1 and {options.MaxPageSize}.");
            size = 1;
        }

        if (page.Number is not null && page.Offset is not null)
        {
            scope.Report("page", QueryErrorCodes.InvalidParameter, "A page number and an offset cannot be combined.");
        }

        if (query.Cursor is not null)
        {
            if (page.Number is not null || page.Offset is not null)
            {
                scope.Report("cursor", QueryErrorCodes.InvalidParameter, "A cursor cannot be combined with a page number or an offset.");
            }

            return (0, size, null, false);
        }

        if (page.Number < 1 || page.Offset < 0)
        {
            scope.Report("page", QueryErrorCodes.InvalidParameter, "A page number starts at 1 and an offset at 0.");
            return (0, size, null, false);
        }

        var skip = page.Number is { } number ? (number - 1L) * size : page.Offset ?? 0L;
        if (skip > options.MaxOffset)
        {
            scope.Report(
                page.Number is null ? "offset" : "page",
                QueryErrorCodes.LimitExceeded,
                $"A page may skip at most {options.MaxOffset} entries. Read further with the cursor of the previous page.");
            skip = 0;
        }

        return ((int)skip, size, page.Offset is null ? page.Number ?? 1 : null, page.WithTotal);
    }

    private SelectNode Selection(QueryScope scope, SchemaType type, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return SelectNode.Everything;
        }

        var root = new SelectNode();
        foreach (var path in paths)
        {
            if (CheckSelection(scope, type, path, 0, out var reverse))
            {
                root.Add(path.Split('.'));
            }
            else if (reverse is (var owner, var relation))
            {
                scope.Report("fields", QueryErrorCodes.ReverseRelation, QueryPaths.ReverseMessage(owner, path, relation));
            }
            else
            {
                scope.Report("fields", QueryErrorCodes.UnknownField, $"'{path}' is not a field of {type.Name} that can be read.");
            }
        }

        return root;
    }

    // reverse is the reverse side the path names where it stops resolving, and the type that has it.
    private bool CheckSelection(QueryScope scope, SchemaType type, string path, int hops, out (SchemaType Owner, ReverseRelation Relation)? reverse)
    {
        reverse = null;
        switch (scope.Paths.Resolve(type, path))
        {
            case SystemPath or GroupPath or FieldPath { Rest.Length: 0 }:
                return true;
            case FieldPath { Stored.Kind: FieldStorageKind.Reference, Stored.Field.Relation: { } relation } hop when hops < options.MaxPopulateDepth:
                var target = schema.FindContentType(relation.Target);
                return target is not null && scope.Access.CanReadType(target) && CheckSelection(scope, target, string.Join('.', hop.Rest), hops + 1, out reverse);
            case null when scope.Paths.Reverse(type, path) is { } named:
                reverse = (type, named);
                return false;
            default:
                return false;
        }
    }

    private PopulateNode Population(QueryScope scope, SchemaType type, ContentQuery query)
    {
        var root = new PopulateNode();
        if (query.PopulateDepth is < 1 || query.PopulateDepth > options.MaxPopulateDepth)
        {
            scope.Report("depth", QueryErrorCodes.LimitExceeded, $"The population depth must be between 1 and {options.MaxPopulateDepth}.");
            return root;
        }

        var depth = query.PopulateDepth ?? 1;
        foreach (var path in query.Populate)
        {
            if (path == All)
            {
                root.AllDepth = depth;
            }
            else if (AddPopulatePath(scope, type, root, path, depth, level: 1) is { } problem)
            {
                scope.Report("populate", problem.Code, problem.Message);
            }
        }

        return root;
    }

    private (string Code, string Message)? AddPopulatePath(QueryScope scope, SchemaType type, PopulateNode node, string path, int depth, int level)
    {
        if (level > options.MaxPopulateDepth)
        {
            return (QueryErrorCodes.LimitExceeded, $"'{path}' goes deeper than the {options.MaxPopulateDepth} levels allowed.");
        }

        var resolved = scope.Paths.Resolve(type, path);
        if (resolved is not FieldPath field)
        {
            return resolved is not null
                ? (QueryErrorCodes.NotARelation, $"'{path}' of {type.Name} is not a relation.")
                : scope.Paths.Reverse(type, path) is { } reverse
                    ? (QueryErrorCodes.ReverseRelation, QueryPaths.ReverseMessage(type, path, reverse))
                    : (QueryErrorCodes.UnknownField, $"'{path}' is not a field of {type.Name} that can be read.");
        }

        // A block list or a repeated component is named to populate the references inside it.
        if (field.Stored.Kind != FieldStorageKind.Reference && !(field.Rest.Length == 0 && EntryPlan.HoldsReferences(field.Stored.Field)))
        {
            return (QueryErrorCodes.NotARelation, $"'{path}' of {type.Name} is not a relation.");
        }

        if (!node.Fields.TryGetValue(field.ApiPath, out var child))
        {
            child = new PopulateNode();
            node.Fields[field.ApiPath] = child;
        }

        if (field.Rest.Length == 0)
        {
            child.AllDepth = Math.Max(child.AllDepth, depth - 1);
            return null;
        }

        var target = field.Stored.Field.Relation is { } relation ? schema.FindContentType(relation.Target) : null;
        if (target is null || !scope.Access.CanReadType(target))
        {
            return (QueryErrorCodes.NotARelation, $"'{field.ApiPath}' of {type.Name} cannot be populated further.");
        }

        return AddPopulatePath(scope, target, child, string.Join('.', field.Rest), depth, level + 1);
    }

    // An entry with the documents its references and the block lists to resolve inside go into, and the ids above it, which
    // a relation must not return to.
    private sealed record Materialized(Guid Id, ContentDocument Document, Dictionary<ReferenceOutput, ContentDocument> Holders, List<(ValueOutput Value, ContentDocument Holder)> Inside, Materialized? Parent)
    {
        public bool HasAncestorOrSelf(Guid id) => Id == id || (Parent?.HasAncestorOrSelf(id) ?? false);
    }

    private static Materialized Materialize(EntryPlan plan, object?[] row, ContentDocument document, Materialized? parent)
    {
        var id = (Guid)row[plan.IdColumn]!;
        if (plan.IsRoot)
        {
            document["id"] = id;
        }

        var entry = new Materialized(id, document, [], [], parent);
        Fill(plan.Nodes, row, document, entry);
        return entry;
    }

    private static void Fill(IEnumerable<OutputNode> nodes, object?[] row, ContentDocument document, Materialized entry)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ValueOutput value:
                    document[value.Name] = value.Read(row[value.Column]);
                    if (value.Inside is not null)
                    {
                        entry.Inside.Add((value, document));
                    }

                    break;
                case ReferenceOutput reference:
                    document[reference.Name] = null;
                    entry.Holders[reference] = document;
                    break;
                case GroupOutput group:
                    var inner = new ContentDocument();
                    Fill(group.Children, row, inner, entry);
                    document[group.Name] = IsAbsent(group, inner) ? null : inner;
                    break;
            }
        }
    }

    // A flattened component has no row of its own: it is absent when nothing below it has a value.
    private static bool IsAbsent(GroupOutput group, ContentDocument document) =>
        group.Children.All(child => child is ValueOutput) && document.Values.All(value => value is null or JsonArray { Count: 0 });

    // knownReferences are those of the one root entry, when its reader has them.
    private async Task FillReferencesAsync(QueryScope query, EntryPlan plan, List<Materialized> entries, ReadableTargets readable, ResolvableReferences resolvable, CancellationToken cancellationToken, IReadOnlyList<Reference>? knownReferences = null)
    {
        foreach (var (value, holder) in entries.SelectMany(entry => entry.Inside))
        {
            resolvable.AddInside(value.Inside!, holder[value.Name] as JsonNode);
        }

        var outputs = plan.References.ToList();
        if (entries.Count == 0 || outputs.Count == 0)
        {
            return;
        }

        var sources = entries.Select(entry => entry.Id).Distinct().ToList();
        var fields = outputs.Select(output => output.ApiPath).ToList();
        var typeName = plan.Type.Name;
        var locale = query.Locale;
        var references = knownReferences is not null
            ? [.. knownReferences.Where(reference => fields.Contains(reference.Field)).OrderBy(reference => reference.Position)]
            : await context.References.AsNoTracking()
                .Where(reference => reference.SourceType == typeName
                    && reference.State == EF.Constant(query.State)
                    && sources.Contains(reference.SourceDocumentId)
                    && fields.Contains(reference.Field)
                    && (reference.Locale == "" || reference.Locale == locale))
                .OrderBy(reference => reference.Position)
                .ToListAsync(cancellationToken);

        var bySource = references.ToLookup(reference => (reference.SourceDocumentId, reference.Field, reference.Locale));

        // The targets of every relation of these entries, and of the relations nested in them, are asked once per type.
        List<(SchemaType Target, IEnumerable<Guid> Ids)> asked = [];
        foreach (var output in outputs)
        {
            if (Target(output) is { } target)
            {
                asked.Add((target, references.Where(reference => reference.Field == output.ApiPath).Select(reference => reference.TargetId)));
            }
        }

        var nested = new NestedTargets(schema);
        foreach (var entry in entries)
        {
            foreach (var (name, ids) in nested.Collect(plan.Type, entry.Document))
            {
                if (schema.FindContentType(name) is { } target)
                {
                    asked.Add((target, ids));
                }
            }
        }

        foreach (var group in asked.GroupBy(item => item.Target))
        {
            await ReadableIdsAsync(query, group.Key, [.. group.SelectMany(item => item.Ids).Distinct()], readable, cancellationToken);
        }

        foreach (var output in outputs)
        {
            var known = await ReadableTargetsAsync(query, output, references, readable, cancellationToken);

            List<(Materialized Parent, ContentDocument Reference, Guid Target)> wanted = [];
            foreach (var entry in entries)
            {
                var values = bySource[(entry.Id, output.ApiPath, output.Stored.Localized ? locale : "")]
                    .Where(reference => known?.Contains(reference.TargetId) != false)
                    .Select(reference => (Reference: Stub(reference), reference.TargetId))
                    .ToList();

                var holder = entry.Holders[output];
                holder[output.Name] = output.Stored.Field.Multiple
                    ? values.Select(value => value.Reference).ToList()
                    : values.Select(value => value.Reference).FirstOrDefault();
                if (output.Resolve)
                {
                    resolvable.AddField(holder, output.Name, output.Stored.Field);
                }

                wanted.AddRange(values
                    .Where(value => output.Plan is not null && !entry.HasAncestorOrSelf(value.TargetId))
                    .Select(value => (entry, value.Reference, value.TargetId)));
            }

            if (output.Plan is { } child && wanted.Count > 0)
            {
                query.CountPopulated(wanted.Count);
                await PopulateAsync(query, child, wanted, readable, resolvable, cancellationToken);
            }
        }
    }

    private static ContentDocument Stub(Reference reference) => new() { ["id"] = reference.TargetId, ["type"] = reference.TargetType };

    // Which targets of a relation to return, or null for all of them. A target the principal cannot read by type or by
    // row rule is not returned, even as a reference, and neither is one in another site than the query's. Under a row
    // rule, a target with no row in this state and locale cannot be shown to pass, so it goes too. A published read shows
    // only what is published (spec 4.5): a target in the trash or with no published row in any locale is not there. A target
    // only in locales the principal's grants do not cover is not there either.
    private async Task<HashSet<Guid>?> ReadableTargetsAsync(QueryScope query, ReferenceOutput output, List<Reference> references, ReadableTargets readable, CancellationToken cancellationToken)
    {
        if (Target(output) is not { } target)
        {
            return null;
        }

        var ids = references.Where(reference => reference.Field == output.ApiPath).Select(reference => reference.TargetId).Distinct().ToList();
        return await ReadableIdsAsync(query, target, ids, readable, cancellationToken);
    }

    private SchemaType? Target(ReferenceOutput output) =>
        output.Stored.Field.Relation is { } relation ? schema.FindContentType(relation.Target) : null;

    private async Task FilterNestedTargetsAsync(QueryScope query, SchemaType type, List<Materialized> entries, ReadableTargets known, CancellationToken cancellationToken)
    {
        var nested = new NestedTargets(schema);
        Dictionary<string, HashSet<Guid>?> readable = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var (name, ids) in nested.Collect(type, entry.Document))
            {
                readable[name] = readable.TryGetValue(name, out var found) ? [.. found ?? [], .. ids] : ids;
            }
        }

        foreach (var (name, ids) in readable.ToList())
        {
            readable[name] = schema.FindContentType(name) is { } target
                ? await ReadableIdsAsync(query, target, [.. ids!], known, cancellationToken)
                : [];
        }

        if (readable.Count > 0)
        {
            foreach (var entry in entries)
            {
                nested.Filter(type, entry.Document, (name, id) => readable[name]?.Contains(id) != false);
            }
        }
    }

    // Asks the database only about the ids this run has not asked about yet. What is returned is to be asked whether it
    // contains an id, and may hold more than the ids given.
    private async Task<HashSet<Guid>?> ReadableIdsAsync(QueryScope query, SchemaType target, List<Guid> ids, ReadableTargets readable, CancellationToken cancellationToken)
    {
        if (readable.TryAnswer(target, ids, out var answer, out var unknown))
        {
            return answer;
        }

        return readable.Add(target, unknown, await ReadableIdsAsync(query, target, unknown, cancellationToken));
    }

    private async Task<HashSet<Guid>?> ReadableIdsAsync(QueryScope query, SchemaType target, List<Guid> ids, CancellationToken cancellationToken)
    {
        if (!query.Access.CanReadType(target))
        {
            return [];
        }

        var inOneSite = target.Scope == ContentScope.Site && query.Site is not null;
        var hasRule = query.Access.RowRule(target) is not null;
        var isPublished = query.State == ContentState.Published;
        var locales = target.Localized ? query.Access.ReadLocales(target) : null;
        if (!inOneSite && !hasRule && !isPublished && locales is null)
        {
            return null;
        }

        if (!hasRule)
        {
            // In a draft a trashed target stays a reference, so this looks past the query filter.
            var documents = isPublished
                ? context.Documents.AsNoTracking().Where(document => document.PublishedVersion != null)
                : context.Documents.IgnoreQueryFilters().AsNoTracking();
            var typeName = target.Name;
            documents = documents.Where(document => ids.Contains(document.DocumentId) && document.Type == typeName);
            if (inOneSite)
            {
                var site = query.Site!.Value.Value;
                documents = documents.Where(document => document.SiteId == site);
            }

            if (locales is null)
            {
                return [.. await documents.Select(document => document.DocumentId).ToListAsync(cancellationToken)];
            }

            var rows = await documents.Select(document => new { document.DocumentId, document.Locale }).ToListAsync(cancellationToken);
            return [.. rows.Where(row => locales.Contains(row.Locale)).Select(row => row.DocumentId)];
        }

        var row = Expression.Parameter(typeof(QueryRow), "row");
        var rule = new FilterCompiler(query).Where(target, null, row)!;
        query.ThrowIfInvalid();

        var readable = await query.Rows(target)
            .Where(item => ids.Contains(item.Document.DocumentId))
            .Where(Lambda(rule, row))
            .Select(item => item.Document.DocumentId)
            .ToListAsync(cancellationToken);

        return [.. readable];
    }

    // A target that has no row in this state and locale stays a reference: in a published read, one published in another locale.
    private async Task PopulateAsync(QueryScope query, EntryPlan plan, List<(Materialized Parent, ContentDocument Reference, Guid Target)> wanted, ReadableTargets readable, ResolvableReferences resolvable, CancellationToken cancellationToken)
    {
        var ids = wanted.Select(item => item.Target).Distinct().ToList();
        var row = plan.Row;
        var rows = query.Rows(plan.Type).Where(item => ids.Contains(item.Document.DocumentId));
        if (new FilterCompiler(query).Where(plan.Type, null, row) is { } rule)
        {
            query.ThrowIfInvalid();
            rows = rows.Where(Lambda(rule, row));
        }

        var byId = (await rows.Select(plan.Projection()).ToListAsync(cancellationToken)).ToDictionary(values => (Guid)values[plan.IdColumn]!);

        List<Materialized> children = [];
        foreach (var (parent, reference, target) in wanted)
        {
            if (byId.TryGetValue(target, out var values))
            {
                children.Add(Materialize(plan, values, reference, parent));
            }
        }

        await FillReferencesAsync(query, plan, children, readable, resolvable, cancellationToken);
    }
}
