using System.Linq.Expressions;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Query;

// The dotted paths of a selection as a tree. With no paths below it, a node selects everything.
internal sealed class SelectNode
{
    public static readonly SelectNode Everything = new() { Whole = true };

    public bool Whole { get; private set; }

    public Dictionary<string, SelectNode> Children { get; } = new(StringComparer.Ordinal);

    public bool Includes(string name) => Whole || Children.ContainsKey(name);

    public SelectNode Child(string name) => Whole ? Everything : Children.GetValueOrDefault(name, Everything);

    public void Add(ReadOnlySpan<string> path)
    {
        if (path.IsEmpty)
        {
            Whole = true;
            return;
        }

        if (!Children.TryGetValue(path[0], out var child))
        {
            child = new SelectNode();
            Children[path[0]] = child;
        }

        child.Add(path[1..]);
    }
}

// Which relations below an entry are inlined. AllDepth is how many levels of every relation are, beyond those named.
internal sealed class PopulateNode
{
    public Dictionary<string, PopulateNode> Fields { get; } = new(StringComparer.Ordinal);

    public int AllDepth { get; set; }

    public PopulateNode? Child(string apiPath)
    {
        var named = Fields.GetValueOrDefault(apiPath);
        if (AllDepth == 0)
        {
            return named;
        }

        var merged = new PopulateNode { AllDepth = Math.Max(named?.AllDepth ?? 0, AllDepth - 1) };
        if (named is not null)
        {
            foreach (var (name, child) in named.Fields)
            {
                merged.Fields[name] = child;
            }
        }

        return merged;
    }
}

internal abstract record OutputNode(string Name);

internal sealed record ValueOutput(string Name, int Column, Func<object?, object?> Read) : OutputNode(Name);

// A single component, flattened into columns of the entry.
internal sealed record GroupOutput(string Name, IReadOnlyList<OutputNode> Children) : OutputNode(Name);

// Plan is null when the relation stays a reference.
internal sealed record ReferenceOutput(string Name, StoredField Stored, string ApiPath, EntryPlan? Plan) : OutputNode(Name);

// Which columns a query reads for an entry and how they become a ContentDocument. Field permissions apply here:
// a field the principal cannot read is left out, not blanked, at every level.
internal sealed class EntryPlan
{
    private readonly QueryScope _query;
    private readonly List<Expression> _columns = [];

    public EntryPlan(QueryScope query, SchemaType type, SelectNode select, PopulateNode populate, int level, bool isRoot, ParameterExpression row)
    {
        _query = query;
        Type = type;
        Row = row;
        IsRoot = isRoot;
        IdColumn = AddColumn(QueryExpressions.DocumentId(row));

        List<OutputNode> nodes = [];
        foreach (var system in SystemField.Fields)
        {
            var wanted = select.Whole ? isRoot && system.Default : select.Includes(system.Name);
            if (wanted && system.Name != "id" && query.Access.CanReadField(type, system.Name))
            {
                nodes.Add(new ValueOutput(system.Name, AddColumn(system.ReadFrom(row, query.State)), value => value));
            }
        }

        foreach (var field in type.Fields)
        {
            if (select.Includes(field.Name) && !field.Hidden && query.Access.CanReadField(type, field.Name)
                && Field(field, "", select.Child(field.Name), populate, level) is { } node)
            {
                nodes.Add(node);
            }
        }

        Nodes = nodes;
    }

    public SchemaType Type { get; }

    public ParameterExpression Row { get; }

    public bool IsRoot { get; }

    public int IdColumn { get; }

    public IReadOnlyList<OutputNode> Nodes { get; }

    public IEnumerable<ReferenceOutput> References => AllReferences(Nodes);

    public int AddColumn(Expression column)
    {
        _columns.Add(column);
        return _columns.Count - 1;
    }

    public Expression<Func<QueryRow, object?[]>> Projection() => Expression.Lambda<Func<QueryRow, object?[]>>(
        Expression.NewArrayInit(typeof(object), _columns.Select(column => Expression.Convert(column, typeof(object)))),
        Row);

    private static IEnumerable<ReferenceOutput> AllReferences(IEnumerable<OutputNode> nodes) => nodes.SelectMany(node => node switch
    {
        ReferenceOutput reference => [reference],
        GroupOutput group => AllReferences(group.Children),
        _ => [],
    });

    private OutputNode? Field(SchemaField field, string prefix, SelectNode select, PopulateNode populate, int level)
    {
        var apiPath = prefix + field.Name;
        var kind = _query.Schema.FieldTypes.Find(field.FieldType)!.StorageKind(field);

        if (field.FieldType == FieldTypeNames.Component && kind == FieldStorageKind.Column)
        {
            var component = _query.Schema.FindComponent((string)field.Settings[FieldSettings.Component])!;
            var children = component.Fields
                .Where(inner => select.Includes(inner.Name) && !inner.Hidden)
                .Select(inner => Field(inner, apiPath + ".", select.Child(inner.Name), populate, level))
                .OfType<OutputNode>()
                .ToList();

            return new GroupOutput(field.Name, children);
        }

        var stored = _query.Paths.Field(Type, apiPath);
        if (kind == FieldStorageKind.Reference)
        {
            return new ReferenceOutput(field.Name, stored, apiPath, ChildPlan(stored, apiPath, select, populate, level));
        }

        var owner = Expression.Property(Row, stored.Localized ? nameof(QueryRow.Localized) : nameof(QueryRow.Shared));
        return new ValueOutput(field.Name, AddColumn(QueryExpressions.Column(owner, stored.Column, stored.ClrType!)), Reader(stored));
    }

    private EntryPlan? ChildPlan(StoredField stored, string apiPath, SelectNode select, PopulateNode populate, int level)
    {
        var child = populate.Child(apiPath);
        var target = stored.Field.Relation is { } relation ? _query.Schema.FindContentType(relation.Target) : null;
        if (child is null || target is null || level >= _query.Options.MaxPopulateDepth || !_query.Access.CanReadType(target))
        {
            return null;
        }

        return new EntryPlan(_query, target, select, child, level + 1, isRoot: false, Expression.Parameter(typeof(QueryRow), "row"));
    }

    private Func<object?, object?> Reader(StoredField stored)
    {
        if (stored.Kind != FieldStorageKind.Json)
        {
            return value => value;
        }

        if (stored.Field.FieldType == FieldTypeNames.RichText)
        {
            return value => value is string json ? new RichText(json) : null;
        }

        return value =>
        {
            var node = value is string json ? JsonNode.Parse(json) : null;
            RemoveHidden(node, stored.Field);
            return node;
        };
    }

    // JSON stores a block or repeated component whole, and a hidden field stays out of every response.
    private void RemoveHidden(JsonNode? value, SchemaField field)
    {
        switch (field.FieldType)
        {
            case FieldTypeNames.Blocks when value is JsonArray blocks:
                foreach (var block in blocks.OfType<JsonObject>())
                {
                    if (block["type"] is JsonValue name && name.TryGetValue<string>(out var blockName) && _query.Schema.FindBlock(blockName) is { } type)
                    {
                        RemoveHidden(block, type);
                    }
                }

                break;
            case FieldTypeNames.Component when _query.Schema.FindComponent((string)field.Settings[FieldSettings.Component]) is { } component:
                foreach (var item in value is JsonArray items ? items.OfType<JsonObject>() : value is JsonObject single ? [single] : [])
                {
                    RemoveHidden(item, component);
                }

                break;
        }
    }

    private void RemoveHidden(JsonObject entry, SchemaType type)
    {
        foreach (var field in type.Fields)
        {
            if (field.Hidden)
            {
                entry.Remove(field.Name);
            }
            else if (entry[field.Name] is { } inner)
            {
                RemoveHidden(inner, field);
            }
        }
    }
}
