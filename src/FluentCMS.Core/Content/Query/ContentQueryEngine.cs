using System.Linq.Expressions;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content.Query;

// Runs a ContentQuery. IContentService (#2693) is its caller, and resolves the locale and site first. Bad input is a
// ValidationException, before anything runs. RichTextFormat and Fallback are carried, not applied: this returns the
// stored rich text and the requested locale's values. LocaleFallbackReader fills the fields that fall back.
internal sealed class ContentQueryEngine(CmsDbContext context, SchemaModel schema, IJsonSqlDialect dialect, IContentQueryAccess access, ContentQueryOptions options)
{
    private const string All = "*";
    private const string EveryLocale = "all";

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
        bool WithTotal);

    public Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, CancellationToken cancellationToken = default) =>
        ExecuteAsync(typeName, query, everySystemField: false, cancellationToken);

    // everySystemField is for a content class, which has a property for each of them; a document leaves out the two that
    // are not in the response shape. Field permissions still decide.
    public Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, bool everySystemField, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query, everySystemField, trashed: false, cancellationToken);

    // Reads the entries in the trash as drafts. Whoever calls this has decided that the principal may look at the trash;
    // what a draft read is limited by still holds.
    public Task<ContentQueryResult> ExecuteTrashAsync(string typeName, ContentQuery query, bool everySystemField, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query with { Status = QueryStatus.Draft }, everySystemField, trashed: true, cancellationToken);

    // Reads the entries the principal may read that another row rule also lets through, such as that of a write about to
    // be made to them. The rule is trusted like the read rule, and holds for the entries read, not for relation targets.
    public Task<ContentQueryResult> ExecuteWithRuleAsync(string typeName, ContentQuery query, ContentFilter? rule, CancellationToken cancellationToken) =>
        ExecuteAsync(typeName, query, everySystemField: false, trashed: false, cancellationToken, rule);

    private async Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, bool everySystemField, bool trashed, CancellationToken cancellationToken, ContentFilter? rule = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        var compiled = Compile(await access.ForQueryAsync(query.Site, query.Locale ?? "", cancellationToken), typeName, query, everySystemField, trashed, rule);

        long? total = compiled.WithTotal ? await compiled.Filtered.LongCountAsync(cancellationToken) : null;
        var rows = await compiled.Rows.ToListAsync(cancellationToken);

        var hasMore = rows.Count > compiled.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var entries = rows.Select(row => Materialize(compiled.Plan, row, new ContentDocument(), parent: null)).ToList();
        await FillReferencesAsync(compiled.Scope, compiled.Plan, entries, cancellationToken);
        await FilterNestedTargetsAsync(compiled.Scope, compiled.Type, entries, cancellationToken);

        var last = hasMore ? rows[^1] : null;
        var nextCursor = last is null ? null : SortKeys.Encode(compiled.Keys, compiled.KeyColumns.Select(column => last[column]).ToList());

        return new(entries.Select(entry => entry.Document).ToList(), total, compiled.PageNumber, compiled.PageSize, nextCursor);
    }

    // Separate from running it, so that a test can read the SQL.
    internal CompiledQuery Compile(string typeName, ContentQuery query) => Compile(access, typeName, query);

    private CompiledQuery Compile(IContentQueryAccess access, string typeName, ContentQuery query, bool everySystemField = false, bool trashed = false, ContentFilter? rule = null)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = query.Status == QueryStatus.Draft ? ContentState.Draft : ContentState.Published;
        var scope = new QueryScope(context, schema, dialect, access, options, state, query.Locale ?? "", query.Site, trashed);

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
        return new(scope, type, rows, page.Select(plan.Projection()), plan, keys, keyColumns, size, number, withTotal);
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
        if (skip > int.MaxValue)
        {
            scope.Report("page", QueryErrorCodes.LimitExceeded, "The page is too far into the list.");
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
            if (CheckSelection(scope, type, path, 0))
            {
                root.Add(path.Split('.'));
            }
            else
            {
                scope.Report("fields", QueryErrorCodes.UnknownField, $"'{path}' is not a field of {type.Name} that can be read.");
            }
        }

        return root;
    }

    private bool CheckSelection(QueryScope scope, SchemaType type, string path, int hops)
    {
        switch (scope.Paths.Resolve(type, path))
        {
            case SystemPath or GroupPath or FieldPath { Rest.Length: 0 }:
                return true;
            case FieldPath { Stored.Kind: FieldStorageKind.Reference, Stored.Field.Relation: { } relation } hop when hops < options.MaxPopulateDepth:
                var target = schema.FindContentType(relation.Target);
                return target is not null && scope.Access.CanReadType(target) && CheckSelection(scope, target, string.Join('.', hop.Rest), hops + 1);
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
            return resolved is null
                ? (QueryErrorCodes.UnknownField, $"'{path}' is not a field of {type.Name} that can be read.")
                : (QueryErrorCodes.NotARelation, $"'{path}' of {type.Name} is not a relation.");
        }

        if (field.Stored.Kind != FieldStorageKind.Reference)
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

    // An entry with the documents its references go into, and the ids above it, which a relation must not return to.
    private sealed record Materialized(Guid Id, ContentDocument Document, Dictionary<ReferenceOutput, ContentDocument> Holders, Materialized? Parent)
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

        var entry = new Materialized(id, document, [], parent);
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

    private async Task FillReferencesAsync(QueryScope query, EntryPlan plan, List<Materialized> entries, CancellationToken cancellationToken)
    {
        var outputs = plan.References.ToList();
        if (entries.Count == 0 || outputs.Count == 0)
        {
            return;
        }

        var sources = entries.Select(entry => entry.Id).Distinct().ToList();
        var fields = outputs.Select(output => output.ApiPath).ToList();
        var typeName = plan.Type.Name;
        var locale = query.Locale;
        var references = await context.References.AsNoTracking()
            .Where(reference => reference.SourceType == typeName
                && reference.State == EF.Constant(query.State)
                && sources.Contains(reference.SourceDocumentId)
                && fields.Contains(reference.Field)
                && (reference.Locale == "" || reference.Locale == locale))
            .OrderBy(reference => reference.Position)
            .ToListAsync(cancellationToken);

        var bySource = references.ToLookup(reference => (reference.SourceDocumentId, reference.Field, reference.Locale));

        foreach (var output in outputs)
        {
            var readable = await ReadableTargetsAsync(query, output, references, cancellationToken);

            List<(Materialized Parent, ContentDocument Reference, Guid Target)> wanted = [];
            foreach (var entry in entries)
            {
                var values = bySource[(entry.Id, output.ApiPath, output.Stored.Localized ? locale : "")]
                    .Where(reference => readable?.Contains(reference.TargetId) != false)
                    .Select(reference => (Reference: Stub(reference), reference.TargetId))
                    .ToList();

                entry.Holders[output][output.Name] = output.Stored.Field.Multiple
                    ? values.Select(value => value.Reference).ToList()
                    : values.Select(value => value.Reference).FirstOrDefault();

                wanted.AddRange(values
                    .Where(value => output.Plan is not null && !entry.HasAncestorOrSelf(value.TargetId))
                    .Select(value => (entry, value.Reference, value.TargetId)));
            }

            if (output.Plan is { } child && wanted.Count > 0)
            {
                query.CountPopulated(wanted.Count);
                await PopulateAsync(query, child, wanted, cancellationToken);
            }
        }
    }

    private static ContentDocument Stub(Reference reference) => new() { ["id"] = reference.TargetId, ["type"] = reference.TargetType };

    // Which targets of a relation to return, or null for all of them. A target the principal cannot read by type or by
    // row rule is not returned, even as a reference, and neither is one in another site than the query's. Under a row
    // rule, a target with no row in this state and locale cannot be shown to pass, so it goes too. A published read shows
    // only what is published (spec 4.5): a target in the trash or with no published row in any locale is not there. A target
    // only in locales the principal's grants do not cover is not there either.
    private async Task<HashSet<Guid>?> ReadableTargetsAsync(QueryScope query, ReferenceOutput output, List<Reference> references, CancellationToken cancellationToken)
    {
        var target = output.Stored.Field.Relation is { } relation ? schema.FindContentType(relation.Target) : null;
        if (target is null)
        {
            return null;
        }

        var ids = references.Where(reference => reference.Field == output.ApiPath).Select(reference => reference.TargetId).Distinct().ToList();
        return await ReadableIdsAsync(query, target, ids, cancellationToken);
    }

    private async Task FilterNestedTargetsAsync(QueryScope query, SchemaType type, List<Materialized> entries, CancellationToken cancellationToken)
    {
        var nested = new NestedTargets(schema);
        Dictionary<string, HashSet<Guid>?> readable = new(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var (name, ids) in nested.Collect(type, entry.Document))
            {
                readable[name] = readable.TryGetValue(name, out var known) ? [.. known ?? [], .. ids] : ids;
            }
        }

        foreach (var (name, ids) in readable.ToList())
        {
            readable[name] = schema.FindContentType(name) is { } target
                ? await ReadableIdsAsync(query, target, [.. ids!], cancellationToken)
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
    private async Task PopulateAsync(QueryScope query, EntryPlan plan, List<(Materialized Parent, ContentDocument Reference, Guid Target)> wanted, CancellationToken cancellationToken)
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

        await FillReferencesAsync(query, plan, children, cancellationToken);
    }
}
