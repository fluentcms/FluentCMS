using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.Mcp;

// The JSON Schema of the arguments of the content tools, from the schema as the caller may read it. A write tool holds
// the fields the caller may write, each as its field type describes it for the OpenAPI document, so its validation, the
// options of a select, the ids of a relation, and the components and blocks follow the schema; a hidden or read-only
// field, which no client sets, is left out. Components and blocks are under $defs.
internal sealed class McpToolSchemas(SchemaModel schema, IFieldTypeRegistry fieldTypes, int defaultPageSize, int maxPageSize)
{
    private const string OpenApiSchemas = "#/components/schemas/";
    private const string ComponentPrefix = "component-";
    private const string BlockPrefix = "block-";

    private const string FilterHelp =
        "Conditions as { \"field\": { \"operator\": value } }; several are joined by and. Operators: eq, ne, lt, lte, gt, gte, contains, startsWith, "
        + "in and nin (an array), between (an array of two), null and notNull (true or false), hasBlock (a block name, on a blocks field). "
        + "\"or\" and \"and\" take an array of such objects and \"not\" takes one. A path through a relation or a component is dotted, such as author.name.";

    public JsonElement List(SchemaType type, bool drafts, bool search)
    {
        JsonObject properties = [];
        if (search)
        {
            properties[McpQueryArguments.Search] = Text("The text to search for, 1 to 256 characters.");
        }

        properties[McpQueryArguments.Filter] = new JsonObject { ["type"] = "object", ["description"] = $"{FilterHelp} Fields: {FieldList(type, withTypes: false)}, and the system fields id, status, createdAt, updatedAt and publishedAt." };
        properties["sort"] = Texts("Field paths to sort by, with a leading - for descending, such as -updatedAt.");
        AddPaging(properties);
        AddRead(properties, type, drafts);
        return Tool(properties, search ? [McpQueryArguments.Search] : []);
    }

    public static JsonElement Get(SchemaType type, bool drafts)
    {
        JsonObject properties = [];
        var singleton = type.Kind == SchemaTypeKind.Singleton;
        if (!singleton)
        {
            properties["id"] = Id($"The id of the {type.Name} entry.");
        }

        AddRead(properties, type, drafts);
        return Tool(properties, singleton ? [] : ["id"]);
    }

    public JsonElement Create(SchemaType type, IReadOnlySet<string> writable)
    {
        Dictionary<string, JsonObject> definitions = new(StringComparer.Ordinal);
        JsonObject properties = new() { ["fields"] = Fields(type, writable, clearable: false, definitions) };
        return Tool(properties, ["fields"], definitions);
    }

    public JsonElement Update(SchemaType type, IReadOnlySet<string> writable)
    {
        Dictionary<string, JsonObject> definitions = new(StringComparer.Ordinal);
        JsonObject properties = [];
        var singleton = type.Kind == SchemaTypeKind.Singleton;
        if (!singleton)
        {
            properties["id"] = Id($"The id of the {type.Name} entry.");
        }

        properties["expectedVersion"] = Integer(
            singleton
                ? "The version the singleton had when it was read, or 0 when it has no entry yet."
                : "The version the entry had when it was read. The update fails when the entry has changed since.",
            minimum: 0);
        properties["fields"] = Fields(type, writable, clearable: true, definitions);
        return Tool(properties, singleton ? ["expectedVersion", "fields"] : ["id", "expectedVersion", "fields"], definitions);
    }

    public static JsonElement Discard(SchemaType type)
    {
        JsonObject properties = [];
        var singleton = type.Kind == SchemaTypeKind.Singleton;
        if (!singleton)
        {
            properties["id"] = Id($"The id of the {type.Name} entry.");
        }

        properties["expectedVersion"] = Integer("The version the entry had when it was read, to discard only what was reviewed.", minimum: 1);
        properties["locale"] = Locale();
        return Tool(properties, singleton ? [] : ["id"]);
    }

    public static JsonElement Delete(SchemaType type)
    {
        JsonObject properties = new() { ["id"] = Id($"The id of the {type.Name} entry.") };
        AddDryRun(properties);
        return Tool(properties, ["id"]);
    }

    public static JsonElement Bulk(IReadOnlyList<string> types, IReadOnlyList<string> actions, int maxItems)
    {
        JsonObject properties = new()
        {
            ["type"] = TypeName(types),
            ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. actions.Select(action => (JsonNode)action)]), ["description"] = "What to do to each entry." },
            ["ids"] = new JsonObject { ["type"] = "array", ["items"] = Id("The id of an entry."), ["minItems"] = 1, ["maxItems"] = maxItems, ["description"] = "The entries, by id. Either ids or filter." },
            [McpQueryArguments.Filter] = new JsonObject { ["type"] = "object", ["description"] = $"The entries, by a filter. Either ids or filter. {FilterHelp}" },
            ["limit"] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = maxItems,
                ["description"] = "The most entries the call may touch. A call that names or matches more is refused, and touches none.",
            },
            ["locale"] = new JsonObject { ["type"] = "string", ["description"] = "The locale to publish or unpublish, such as fr-CA; the locale of the connection when left out. Not for a delete." },
        };
        AddDryRun(properties);
        return Tool(properties, ["type", "action", "limit"]);
    }

    public JsonElement Query(IReadOnlyList<string> types, bool search)
    {
        JsonObject properties = new() { ["type"] = TypeName(types) };
        if (search)
        {
            properties[McpQueryArguments.Search] = Text("A text to search for, 1 to 256 characters.");
        }

        properties[McpQueryArguments.Filter] = new JsonObject { ["type"] = "object", ["description"] = FilterHelp };
        properties["sort"] = Texts("Field paths to sort by, with a leading - for descending, such as -updatedAt.");
        AddPaging(properties);
        properties["fields"] = Texts("The fields to return; all when left out.");
        properties["populate"] = Texts("The relations to return as entries rather than ids, or * for all.");
        properties["depth"] = Integer("How deep populate goes, 1 when left out.", minimum: 1);
        properties["status"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("draft", "published"), ["description"] = "The draft, where the caller may read drafts, or what is published." };
        properties["locale"] = Locale();
        return Tool(properties, ["type"]);
    }

    public static JsonElement Publish(IReadOnlyList<string> types)
    {
        JsonObject properties = new()
        {
            ["type"] = TypeName(types),
            ["id"] = Id("The id of the entry."),
            ["expectedVersion"] = Integer("The version the entry had when it was read, to publish only what was reviewed.", minimum: 1),
            ["locale"] = Locale(),
        };
        return Tool(properties, ["type", "id"]);
    }

    public static JsonElement Unpublish(IReadOnlyList<string> types)
    {
        JsonObject properties = new()
        {
            ["type"] = TypeName(types),
            ["id"] = Id("The id of the entry."),
            ["locale"] = Locale(),
        };
        return Tool(properties, ["type", "id"]);
    }

    public static JsonElement Schedule(IReadOnlyList<string> types, IReadOnlyList<string> actions)
    {
        JsonObject properties = new()
        {
            ["type"] = TypeName(types),
            ["id"] = Id("The id of the entry."),
            ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. actions.Select(action => (JsonNode)action)]) },
            ["at"] = Text("When to run: an instant with an offset, such as 2027-03-01T09:00:00Z, or with timeZone the wall-clock time there, such as 2027-03-01T09:00:00."),
            ["timeZone"] = Text("An IANA time zone, such as Europe/Berlin, when at is a wall-clock time."),
            ["locale"] = Locale(),
        };
        return Tool(properties, ["type", "id", "action", "at"]);
    }

    // The fields of a type by name, for a description: "title (text), author (relation to author), tags (list of select)".
    public static string FieldList(SchemaType type, bool withTypes = true)
    {
        var fields = type.Fields.Where(field => !field.Hidden).Select(field => withTypes ? $"{field.Name} ({Kind(field)})" : field.Name).ToList();
        return fields.Count > 0 ? string.Join(", ", fields) : "none";
    }

    private static string Kind(SchemaField field)
    {
        var kind = field.Relation is { } relation ? $"{field.FieldType} to {relation.Target}" : field.FieldType;
        return field.Multiple ? $"list of {kind}" : kind;
    }

    private void AddPaging(JsonObject properties)
    {
        properties["page"] = Integer("The page, from 1.", minimum: 1);
        properties["pageSize"] = new JsonObject
        {
            ["type"] = "integer",
            ["minimum"] = 1,
            ["maximum"] = maxPageSize,
            ["description"] = $"The entries on a page, {defaultPageSize} when left out and {maxPageSize} at most.",
        };
        properties["cursor"] = Text("The nextCursor of the answer before, in place of page. An answer that would be too long ends early, with truncated true, and its nextCursor goes on from there.");
        properties["withTotal"] = new JsonObject { ["type"] = "boolean", ["description"] = "False leaves the total out, which saves a count." };
    }

    private static void AddDryRun(JsonObject properties)
    {
        properties[McpConfirmations.DryRun] = new JsonObject
        {
            ["type"] = "boolean",
            ["default"] = true,
            ["description"] = "True, the default, changes nothing and answers the plan with a confirmationToken. False runs the call, and needs that token.",
        };
        properties[McpConfirmations.Token] = Text("The confirmationToken of the dry run of this same call, which lasts five minutes. Needed when dryRun is false.");
    }

    private static void AddRead(JsonObject properties, SchemaType type, bool drafts)
    {
        properties["fields"] = Texts($"The fields to return; all when left out. Fields: {FieldList(type)}.");
        properties["populate"] = Texts("The relations to return as entries rather than ids, or * for all.");
        properties["depth"] = Integer("How deep populate goes, 1 when left out.", minimum: 1);
        properties["status"] = drafts
            ? new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("draft", "published"), ["description"] = "The draft, which is the default, or what is published." }
            : new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("published"), ["description"] = "What is published; the caller reads no drafts." };
        if (type.Localized)
        {
            properties["locale"] = Locale();
        }
    }

    private JsonObject Fields(SchemaType type, IReadOnlySet<string> writable, bool clearable, Dictionary<string, JsonObject> definitions)
    {
        JsonObject properties = [];
        foreach (var field in type.Fields.Where(field => Settable(field) && writable.Contains(field.Name)))
        {
            properties[field.Name] = Field(field, clearable, definitions);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = clearable
                ? $"The fields of {type.Name} to change. A field left out keeps its value, and null clears one."
                : $"The fields of {type.Name}. Any may be left out of a draft.",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
    }

    private static bool Settable(SchemaField field) => !field.Hidden && !field.ReadOnly;

    private JsonObject Field(SchemaField field, bool clearable, Dictionary<string, JsonObject> definitions)
    {
        var fragment = Fragment(field, definitions);
        if (clearable && !field.Multiple && fragment["type"] is JsonValue type && type.TryGetValue<string>(out var name))
        {
            fragment["type"] = new JsonArray(name, "null");
            (fragment["enum"] as JsonArray)?.Add(null);
        }

        List<string> notes = [];
        if (field.Display?.Help is { Length: > 0 } help)
        {
            notes.Add(help.EndsWith('.') ? help : help + ".");
        }

        if (field.Relation is { } relation)
        {
            notes.Add(field.Multiple ? $"The ids of {relation.Target} entries." : $"The id of a {relation.Target} entry.");
        }

        if (field.Required)
        {
            notes.Add("Required to publish.");
        }

        if (field.Unique)
        {
            notes.Add("Unique among the entries.");
        }

        if (notes.Count > 0)
        {
            fragment["description"] = fragment["description"] is JsonValue described && described.TryGetValue<string>(out var text)
                ? $"{text} {string.Join(' ', notes)}"
                : string.Join(' ', notes);
        }

        return fragment;
    }

    private JsonObject Fragment(SchemaField field, Dictionary<string, JsonObject> definitions)
    {
        // Localize changes the fragment, and a field type may hand out the same node every time.
        var fragment = fieldTypes.Find(field.FieldType)?.OpenApiSchema(field).DeepClone().AsObject() ?? [];
        Localize(fragment, definitions);
        return fragment;
    }

    // A fragment points to components and blocks as the OpenAPI document names them. Here they are under $defs, added as
    // they are met, and the discriminator of a block list, which only OpenAPI has, goes.
    private void Localize(JsonNode? node, Dictionary<string, JsonObject> definitions)
    {
        switch (node)
        {
            case JsonObject properties:
                properties.Remove("discriminator");
                if (properties["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var target) && target.StartsWith(OpenApiSchemas, StringComparison.Ordinal))
                {
                    var name = target[OpenApiSchemas.Length..];
                    properties["$ref"] = $"#/$defs/{name}";
                    Define(name, definitions);
                }

                foreach (var (_, value) in properties.ToList())
                {
                    Localize(value, definitions);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    Localize(item, definitions);
                }

                break;
        }
    }

    private void Define(string name, Dictionary<string, JsonObject> definitions)
    {
        if (definitions.ContainsKey(name))
        {
            return;
        }

        // Set before its fields are read, so that a component that holds itself ends.
        var definition = definitions[name] = new JsonObject { ["type"] = "object" };
        var block = name.StartsWith(BlockPrefix, StringComparison.Ordinal) ? schema.FindBlock(name[BlockPrefix.Length..]) : null;
        var part = block ?? (name.StartsWith(ComponentPrefix, StringComparison.Ordinal) ? schema.FindComponent(name[ComponentPrefix.Length..]) : null);
        if (part is null)
        {
            return;
        }

        JsonObject properties = [];
        if (block is not null)
        {
            properties["type"] = new JsonObject { ["type"] = "string", ["const"] = block.Name };
            properties["id"] = new JsonObject { ["type"] = "string", ["description"] = "The id of the block as it was read. A new block has none." };
            definition["required"] = new JsonArray("type");
        }

        foreach (var field in part.Fields.Where(Settable))
        {
            properties[field.Name] = Field(field, clearable: false, definitions);
        }

        definition["properties"] = properties;
    }

    private static JsonElement Tool(JsonObject properties, string[] required, Dictionary<string, JsonObject>? definitions = null)
    {
        JsonObject tool = new() { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0)
        {
            tool["required"] = new JsonArray([.. required.Select(name => (JsonNode)name)]);
        }

        tool["additionalProperties"] = false;
        if (definitions is { Count: > 0 })
        {
            JsonObject defs = [];
            foreach (var (name, definition) in definitions.OrderBy(definition => definition.Key, StringComparer.Ordinal))
            {
                defs[name] = definition;
            }

            tool["$defs"] = defs;
        }

        return McpArguments.Element(tool);
    }

    private static JsonObject TypeName(IReadOnlyList<string> types) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. types.Select(type => (JsonNode)type)]), ["description"] = "The content type." };

    private static JsonObject Id(string description) => new() { ["type"] = "string", ["format"] = "uuid", ["description"] = description };

    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Texts(string description) =>
        new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description };

    private static JsonObject Integer(string description, int minimum) =>
        new() { ["type"] = "integer", ["minimum"] = minimum, ["description"] = description };

    private static JsonObject Locale() =>
        new() { ["type"] = "string", ["description"] = "A locale of the site, such as fr-CA; the locale of the connection when left out." };
}
