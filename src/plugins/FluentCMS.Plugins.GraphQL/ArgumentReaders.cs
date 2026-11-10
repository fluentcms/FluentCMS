using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using HotChocolate.Language;

namespace FluentCMS.Plugins.GraphQL;

internal enum SortDirection
{
    Asc,
    Desc,
}

internal enum RichTextFormat
{
    Json,
    Html,
    Markdown,
}

// A filter input as the AST the REST bracket parser builds for the same conditions: a text operand as REST sends it, the
// conditions of one level joined by And, and a relation's or a component's fields as dotted paths.
internal static class FilterReader
{
    public const string And = "and";
    public const string Or = "or";
    public const string Not = "not";
    public const string Where = "where";

    public static readonly FrozenDictionary<string, FilterOperator> Operators = new Dictionary<string, FilterOperator>(StringComparer.Ordinal)
    {
        ["eq"] = FilterOperator.Equal,
        ["ne"] = FilterOperator.NotEqual,
        ["in"] = FilterOperator.In,
        ["nin"] = FilterOperator.NotIn,
        ["lt"] = FilterOperator.LessThan,
        ["lte"] = FilterOperator.LessThanOrEqual,
        ["gt"] = FilterOperator.GreaterThan,
        ["gte"] = FilterOperator.GreaterThanOrEqual,
        ["between"] = FilterOperator.Between,
        ["contains"] = FilterOperator.Contains,
        ["startsWith"] = FilterOperator.StartsWith,
        ["null"] = FilterOperator.IsNull,
        ["notNull"] = FilterOperator.IsNotNull,
        ["hasBlock"] = FilterOperator.HasBlock,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string NameOf(FilterOperator filterOperator) => Operators.First(item => item.Value == filterOperator).Key;

    public static ContentFilter? Read(IValueNode? literal, TypeModel type) =>
        literal is ObjectValueNode node ? Node(node, type, "") : null;

    private static ContentFilter? Node(ObjectValueNode node, TypeModel type, string prefix)
    {
        List<ContentFilter> nodes = [];
        foreach (var field in node.Fields)
        {
            var name = field.Name.Value;
            switch (field.Value)
            {
                case ListValueNode list when name is And or Or:
                    var group = list.Items.OfType<ObjectValueNode>().Select(item => Node(item, type, prefix)).OfType<ContentFilter>().ToList();
                    if (group.Count > 0)
                    {
                        nodes.Add(name == Or ? new Or(group) : Single(group) ?? new And(group));
                    }

                    break;
                case ObjectValueNode inner when name == Not:
                    if (Node(inner, type, prefix) is { } negated)
                    {
                        nodes.Add(new Not(negated));
                    }

                    break;
                case ObjectValueNode conditions:
                    Field(nodes, conditions, type, prefix + name, name);
                    break;
            }
        }

        return Single(nodes) ?? (nodes.Count > 0 ? new And(nodes) : null);
    }

    private static ContentFilter? Single(List<ContentFilter> nodes) => nodes.Count == 1 ? nodes[0] : null;

    private static void Field(List<ContentFilter> nodes, ObjectValueNode conditions, TypeModel type, string path, string name)
    {
        if (type.Role == TypeRole.Content && SystemFieldModel.Find(name) is { } system)
        {
            // REST names a status in lower case.
            Func<IValueNode, string?> text = system.Name == SystemFieldModel.Status
                ? literal => EntryValues.ToText(literal)?.ToLowerInvariant()
                : EntryValues.ToText;
            foreach (var condition in conditions.Fields)
            {
                Leaf(nodes, path, condition, text);
            }

            return;
        }

        if (type.Find(name) is not { } field)
        {
            return;
        }

        if (field is { Kind: FieldKind.Component, Multiple: false })
        {
            if (Node(conditions, field.Target!, path + ".") is { } inner)
            {
                nodes.Add(inner);
            }

            return;
        }

        foreach (var condition in conditions.Fields)
        {
            if (field.Kind == FieldKind.Relation && condition.Name.Value == Where)
            {
                if (condition.Value is ObjectValueNode where && Node(where, field.Target!, path + ".") is { } inner)
                {
                    nodes.Add(inner);
                }
            }
            else
            {
                Leaf(nodes, path, condition, literal => literal is EnumValueNode option && field.OptionsByName.TryGetValue(option.Value, out var value) ? value : EntryValues.ToText(literal));
            }
        }
    }

    private static void Leaf(List<ContentFilter> nodes, string path, ObjectFieldNode condition, Func<IValueNode, string?> text)
    {
        if (condition.Value is NullValueNode || !Operators.TryGetValue(condition.Name.Value, out var filterOperator))
        {
            return;
        }

        switch (filterOperator)
        {
            case FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between:
                var items = (condition.Value is ListValueNode list ? list.Items : [condition.Value]).Select(text).Cast<object?>().ToList();
                if (filterOperator == FilterOperator.Between && items.Count != 2)
                {
                    throw new ValidationException([new ValidationError(path, RestErrorCodes.InvalidValue, "between needs two values.")]);
                }

                nodes.Add(new FieldFilter(path, filterOperator, items));
                break;
            case FilterOperator.IsNull or FilterOperator.IsNotNull:
                var asked = condition.Value is BooleanValueNode { Value: true };
                nodes.Add(new FieldFilter(path, asked == (filterOperator == FilterOperator.IsNull) ? FilterOperator.IsNull : FilterOperator.IsNotNull));
                break;
            default:
                nodes.Add(new FieldFilter(path, filterOperator, text(condition.Value)));
                break;
        }
    }
}

internal static class SortReader
{
    public const string FieldKey = "field";
    public const string DirectionKey = "direction";

    // paths has the path of each value of the type's sort enum.
    public static IReadOnlyList<SortField> Read(IValueNode? literal, IReadOnlyDictionary<string, string> paths)
    {
        var items = literal switch
        {
            ListValueNode list => list.Items,
            ObjectValueNode one => [one],
            _ => [],
        };

        List<SortField> sort = [];
        foreach (var item in items.OfType<ObjectValueNode>())
        {
            var key = item.Fields.FirstOrDefault(field => field.Name.Value == FieldKey)?.Value;
            if (key is EnumValueNode name && paths.TryGetValue(name.Value, out var path))
            {
                var direction = item.Fields.FirstOrDefault(field => field.Name.Value == DirectionKey)?.Value;
                sort.Add(new SortField(path, direction is EnumValueNode { Value: "DESC" }));
            }
        }

        return sort;
    }
}

// The input of a create or an update as the document the Management API reads from a body: every value stays JSON, which
// the content service reads against the fields of the type. A key the input leaves out is not in the document.
internal static class InputReader
{
    public static ContentDocument Read(IValueNode? literal, TypeModel type)
    {
        ContentDocument entry = [];
        if (literal is ObjectValueNode input)
        {
            foreach (var (name, value) in Object(input, type))
            {
                entry[name] = JsonSerializer.SerializeToElement(value);
            }
        }

        return entry;
    }

    private static JsonObject Object(ObjectValueNode input, TypeModel type)
    {
        JsonObject values = [];
        foreach (var field in input.Fields)
        {
            var name = field.Name.Value;
            values[name] = type.Find(name) is { } model ? Value(field.Value, model) : EntryValues.ToJson(field.Value);
        }

        return values;
    }

    private static JsonNode? Value(IValueNode literal, FieldModel field) => (field.Kind, literal) switch
    {
        (_, ListValueNode list) => new JsonArray([.. list.Items.Select(item => Value(item, field))]),
        (FieldKind.Enum, EnumValueNode option) => JsonValue.Create(field.OptionsByName.TryGetValue(option.Value, out var value) ? value : option.Value),
        (FieldKind.Component, ObjectValueNode component) => Object(component, field.Target!),
        _ => EntryValues.ToJson(literal),
    };
}
