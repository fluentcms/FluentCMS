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
// stored rich text and the requested locale's values (#2691).
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

    public async Task<ContentQueryResult> ExecuteAsync(string typeName, ContentQuery query, CancellationToken cancellationToken = default)
    {
        var compiled = Compile(typeName, query);

        long? total = compiled.WithTotal ? await compiled.Filtered.LongCountAsync(cancellationToken) : null;
        var rows = await compiled.Rows.ToListAsync(cancellationToken);

        var hasMore = rows.Count > compiled.PageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var entries = rows.Select(row => Materialize(compiled.Plan, row, new ContentDocument(), parent: null)).ToList();
        await FillReferencesAsync(compiled.Scope, compiled.Plan, entries, cancellationToken);

        var last = hasMore ? rows[^1] : null;
        var nextCursor = last is null ? null : SortKeys.Encode(compiled.Keys, compiled.KeyColumns.Select(column => last[column]).ToList());

        return new(entries.Select(entry => entry.Document).ToList(), total, compiled.PageNumber, compiled.PageSize, nextCursor);
    }

    // Separate from running it, so that a test can read the SQL.
    internal CompiledQuery Compile(string typeName, ContentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = query.Status == QueryStatus.Draft ? ContentState.Draft : ContentState.Published;
        var scope = new QueryScope(context, schema, dialect, access, options, state, query.Locale ?? "");

        var type = schema.FindContentType(typeName);
        if (type is null || !access.CanReadType(type))
        {
            scope.Report("type", QueryErrorCodes.UnknownField, $"'{typeName}' is not a content type that can be read.");
            scope.ThrowIfInvalid();
        }

        if (type!.Localized && (string.IsNullOrEmpty(query.Locale) || query.Locale == EveryLocale))
        {
            scope.Report("locale", QueryErrorCodes.InvalidParameter, $"A list of {type.Name} needs one locale.");
        }

        var row = Expression.Parameter(typeof(QueryRow), "row");
        var rows = scope.Rows(type);

        if (query.Site is { } site && type.Scope == ContentScope.Site)
        {
            rows = rows.Where(Lambda(Expression.Equal(QueryExpressions.Column(Expression.Property(row, nameof(QueryRow.Shared)), StorageNames.SiteId, typeof(Guid)), QueryExpressions.Bind(site.Value)), row));
        }

        if (new FilterCompiler(scope).Where(type, query.Filter, row) is { } filter)
        {
            rows = rows.Where(Lambda(filter, row));
        }

        var plan = new EntryPlan(scope, type, Selection(scope, type, query.Select), Population(scope, type, query), level: 0, isRoot: true, row);
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
                return target is not null && access.CanReadType(target) && CheckSelection(scope, target, string.Join('.', hop.Rest), hops + 1);
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
        if (target is null || !access.CanReadType(target))
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
            List<(Materialized Parent, ContentDocument Reference, Guid Target)> wanted = [];
            foreach (var entry in entries)
            {
                var values = bySource[(entry.Id, output.ApiPath, output.Stored.Localized ? locale : "")]
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
                await PopulateAsync(query, child, wanted, cancellationToken);
            }
        }
    }

    private static ContentDocument Stub(Reference reference) => new() { ["id"] = reference.TargetId, ["type"] = reference.TargetType };

    // A target the principal may not see, or one that has no row in this state and locale, stays a reference.
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
