using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.GraphQL;

internal enum TypeRole
{
    Content,
    Component,
    Block,
}

internal enum FieldKind
{
    Scalar,
    Enum,
    Json,
    RichText,
    Relation,
    Component,
    Blocks,
    Reference,
}

// A field of the content schema as GraphQL shows it. Scalar is the name of its GraphQL scalar, Target the content type of a
// relation or the component, and Blocks the union of a block list.
internal sealed class FieldModel(SchemaField field, FieldKind kind)
{
    public SchemaField Field { get; } = field;

    public string Name => Field.Name;

    public FieldKind Kind { get; } = kind;

    public bool Multiple { get; init; }

    public string Scalar { get; init; } = ScalarNames.String;

    public TypeModel? Target { get; set; }

    public string? TypeName { get; init; }

    public IReadOnlyList<string> BlockNames { get; init; } = [];

    // The options of a select by the name of their enum value, and back.
    public IReadOnlyDictionary<string, string> OptionsByName { get; init; } = new Dictionary<string, string>();

    public IReadOnlySet<FilterOperator> Operators { get; init; } = new HashSet<FilterOperator>();

    public bool Sortable { get; init; }

    public bool Writable => !Field.ReadOnly;

    // A relation inside a block or a repeated component, which is stored whole: a read never populates it.
    public bool ReferenceOnly { get; set; }

    // A block list or a repeated component is stored whole, so the references inside it are populated by naming the field.
    public bool HoldsReferences => Kind == FieldKind.Blocks || (Kind == FieldKind.Component && Multiple);
}

internal sealed class TypeModel(SchemaType type, TypeRole role, string name)
{
    private Dictionary<string, FieldModel> _byName = [];

    public SchemaType Schema { get; } = type;

    public TypeRole Role { get; } = role;

    // The name of the GraphQL object type.
    public string Name { get; } = name;

    // The name in the content schema.
    public string SchemaName => Schema.Name;

    public IReadOnlyList<FieldModel> Fields
    {
        get;
        set
        {
            field = value;
            _byName = value.ToDictionary(item => item.Name, StringComparer.Ordinal);
        }
    } = [];

    public FieldModel? Find(string name) => _byName.GetValueOrDefault(name);
}

internal static class ScalarNames
{
    public const string String = "String";
    public const string Int = "Int";
    public const string Long = "Long";
    public const string Decimal = "Decimal";
    public const string Boolean = "Boolean";
    public const string Id = "ID";
    public const string DateTime = "DateTime";
    public const string Date = "Date";
    public const string Json = "JSON";

    public static IReadOnlyList<string> All { get; } = [String, Int, Long, Decimal, Boolean, Id, DateTime, Date, Json, "Float"];
}

// The fields every entry has beside its own, as the REST response names them, with the operators the REST filter takes.
internal sealed record SystemFieldModel(string Name, string Scalar, IReadOnlyList<FilterOperator> Operators, bool Sortable, bool Selectable = true)
{
    private static readonly FilterOperator[] Identity = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn];

    private static readonly FilterOperator[] Ordered =
    [
        FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn,
        FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
        FilterOperator.Between, FilterOperator.IsNull, FilterOperator.IsNotNull,
    ];

    public const string Status = "status";

    public static IReadOnlyList<SystemFieldModel> All { get; } =
    [
        new("id", ScalarNames.Id, Identity, Sortable: true),
        new(Status, ScalarNames.String, Identity, Sortable: false),
        new("locale", ScalarNames.String, [], Sortable: false),
        new("version", ScalarNames.Int, [.. Ordered.Where(item => item is not (FilterOperator.IsNull or FilterOperator.IsNotNull))], Sortable: false),
        new("createdAt", ScalarNames.DateTime, Ordered, Sortable: true),
        new("updatedAt", ScalarNames.DateTime, Ordered, Sortable: true),
        new("publishedAt", ScalarNames.DateTime, Ordered, Sortable: true),
        new("createdBy", ScalarNames.Id, Identity, Sortable: false, Selectable: false),
        new("updatedBy", ScalarNames.Id, Identity, Sortable: false, Selectable: false),
    ];

    public static SystemFieldModel? Find(string name) => All.FirstOrDefault(field => field.Name == name);
}

// The content schema read once for GraphQL: every content type, component and block with the GraphQL name and shape of
// its fields. Hidden fields are left out, as every response leaves them out.
internal sealed class ContentModel
{
    public const string EntryReference = "EntryReference";
    public const string PublishWarningType = "PublishWarning";

    // The names the schema has before any content type: the root types, the scalars and the plugin's own types.
    private static readonly string[] Reserved =
    [
        "Query", "Mutation", "Subscription", EntryReference, PublishWarningType, "UUID", .. ScalarNames.All,
        nameof(Publishing.EntryStatus), nameof(QueryStatus), nameof(LocaleFallback), nameof(RichTextFormat), nameof(SortDirection),
    ];

    private const string ComponentType = "component";
    private const string BlocksType = "blocks";
    private const string RichTextType = "richText";
    private const string SelectType = "select";

    private readonly Dictionary<string, TypeModel> _content = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeModel> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeModel> _blocks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public ContentModel(SchemaModel schema, IFieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        _names.UnionWith(Reserved);

        foreach (var type in schema.ContentTypes)
        {
            _content[type.Name] = new TypeModel(type, TypeRole.Content, Claim(GraphQLNames.Pascal(type.Name), type.Name));
        }

        foreach (var type in schema.Components)
        {
            _components[type.Name] = new TypeModel(type, TypeRole.Component, Claim(GraphQLNames.Pascal(type.Name) + "Component", type.Name));
        }

        foreach (var type in schema.Blocks)
        {
            _blocks[type.Name] = new TypeModel(type, TypeRole.Block, Claim(GraphQLNames.Pascal(type.Name) + "Block", type.Name));
        }

        var all = ContentTypes.Concat(Components).Concat(Blocks).ToList();
        foreach (var type in all)
        {
            type.Fields = [.. type.Schema.Fields.Where(field => !field.Hidden).Select(field => Field(type, field, fieldTypes))];
        }

        var uses = all.SelectMany(type => type.Fields.Select(field => (Type: type, Field: field))).ToList();
        foreach (var type in all.Where(type => type.Role == TypeRole.Block
            || uses.Any(use => use.Field.Kind == FieldKind.Component && use.Field.Target == type && (use.Field.Multiple || use.Type.Role == TypeRole.Block))))
        {
            foreach (var field in type.Fields.Where(field => field.Kind == FieldKind.Relation))
            {
                field.ReferenceOnly = true;
            }
        }
    }

    public IEnumerable<TypeModel> ContentTypes => _content.Values;

    public IEnumerable<TypeModel> Components => _components.Values;

    public IEnumerable<TypeModel> Blocks => _blocks.Values;

    // The names of the GraphQL types the model has taken.
    public IReadOnlySet<string> Names => _names;

    public TypeModel? FindBlock(string name) => _blocks.GetValueOrDefault(name);

    // Two names of the content schema may give one GraphQL name, such as a type named article-list beside the list of
    // article. The host stops rather than serve a schema that leaves one of them out.
    public static string Claim(HashSet<string> names, string name, string source) => names.Add(name)
        ? name
        : throw new PluginException(GraphQLErrorCodes.SchemaConflict, $"{GraphQLErrorCodes.SchemaConflict}: The GraphQL name '{name}', made for '{source}', is already taken by another type of the content schema. Rename one of the two there.");

    private string Claim(string name, string source) => Claim(_names, name, source);

    private FieldModel Field(TypeModel owner, SchemaField field, IFieldTypeRegistry fieldTypes)
    {
        var fieldType = fieldTypes.Find(field.FieldType);
        var operators = fieldType?.FilterOperators ?? new HashSet<FilterOperator>();
        var sortable = fieldType?.Sortable(field) == true;

        if (field.Relation is { } relation && _content.TryGetValue(relation.Target, out var target))
        {
            return new FieldModel(field, FieldKind.Relation) { Multiple = field.Multiple, Target = target, Scalar = ScalarNames.Id, Operators = operators };
        }

        switch (field.FieldType)
        {
            case ComponentType when field.Settings.TryGetValue(FieldSettings.Component, out var name) && name is string component && _components.TryGetValue(component, out var found):
                return new FieldModel(field, FieldKind.Component) { Multiple = field.Multiple, Target = found };
            case BlocksType when BlocksOf(field) is { Count: > 0 } blocks:
                return new FieldModel(field, FieldKind.Blocks)
                {
                    Multiple = true,
                    BlockNames = blocks,
                    TypeName = Claim($"{owner.Name}{GraphQLNames.Pascal(field.Name)}Block", $"{owner.SchemaName}.{field.Name}"),
                    Operators = operators,
                };
            case RichTextType:
                return new FieldModel(field, FieldKind.RichText) { Scalar = ScalarNames.Json, Operators = operators };
            case SelectType when Options(field) is { Count: > 0 } options:
                return new FieldModel(field, FieldKind.Enum)
                {
                    Multiple = field.Multiple,
                    OptionsByName = options,
                    TypeName = Claim($"{owner.Name}{GraphQLNames.Pascal(field.Name)}", $"{owner.SchemaName}.{field.Name}"),
                    Operators = operators,
                    Sortable = sortable,
                };
        }

        // A plugin's field type stored as a reference, such as media, whose value a read populates.
        if (fieldType?.StorageKind(field) == FieldStorageKind.Reference)
        {
            return new FieldModel(field, FieldKind.Reference) { Multiple = field.Multiple, Scalar = ScalarNames.Id, Operators = operators };
        }

        var fragment = fieldType?.OpenApiSchema(field) ?? [];
        var multiple = Text(fragment, "type") == "array";
        var scalar = ScalarOf(multiple && fragment["items"] is JsonObject items ? items : fragment);
        return scalar is null
            ? new FieldModel(field, FieldKind.Json) { Scalar = ScalarNames.Json, Operators = operators }
            : new FieldModel(field, FieldKind.Scalar) { Multiple = multiple, Scalar = scalar, Operators = operators, Sortable = sortable };
    }

    private List<string> BlocksOf(SchemaField field) =>
        [.. (field.Settings.TryGetValue(FieldSettings.Blocks, out var list) && list is IEnumerable<string> names ? names : []).Where(_blocks.ContainsKey)];

    // The enum of a select, or null when an option cannot be the name of an enum value or two options would share one:
    // the field is then a string, as REST has it.
    private static Dictionary<string, string>? Options(SchemaField field)
    {
        if (!field.Settings.TryGetValue(FieldSettings.Options, out var value) || value is not IEnumerable<string> options)
        {
            return null;
        }

        Dictionary<string, string> byName = new(StringComparer.Ordinal);
        foreach (var option in options)
        {
            if (GraphQLNames.EnumValue(option) is not { } name || !byName.TryAdd(name, option))
            {
                return null;
            }
        }

        return byName;
    }

    // The scalar of a field type by the JSON schema it gives the OpenAPI document, so that a plugin's field type has one too.
    private static string? ScalarOf(JsonObject fragment) => (Text(fragment, "type"), Text(fragment, "format")) switch
    {
        ("string", "date-time") => ScalarNames.DateTime,
        ("string", "date") => ScalarNames.Date,
        ("string", "uuid") => ScalarNames.Id,
        ("string", _) => ScalarNames.String,
        ("integer", "int32") => ScalarNames.Int,
        ("integer", _) => ScalarNames.Long,
        ("number", _) => ScalarNames.Decimal,
        ("boolean", _) => ScalarNames.Boolean,
        _ => null,
    };

    private static string? Text(JsonObject fragment, string key) =>
        fragment[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
