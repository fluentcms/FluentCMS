using System.Text.Json.Nodes;
using FluentCMS.Core.ProseMirror;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Publishing;
using FluentCMS.Schema;

namespace FluentCMS.Core.Api.OpenApi;

// The schemas under #/components/schemas that the live schema gives: an entry as read (entry-{type}) and as written
// (input-{type}) per content type, and one per component and block, each field described by its field type's fragment.
// No field is required, because a draft may leave any out and fields= selects some.
internal sealed class ContentSchemas(SchemaModel schema, OpenApiDocumentOptions options)
{
    private const string BlockDiscriminator = "type";
    private const string BlockId = "id";

    public IEnumerable<(string Name, JsonObject Schema)> All()
    {
        foreach (var type in schema.ContentTypes)
        {
            yield return (OpenApiNames.EntrySchema(type.Name), Entry(type));
            yield return (OpenApiNames.InputSchema(type.Name), Input(type));
        }

        foreach (var component in schema.Components)
        {
            yield return (OpenApiNames.ComponentSchema(component.Name), Fields(component, $"The {component.Name} component."));
        }

        foreach (var block in schema.Blocks)
        {
            yield return (OpenApiNames.BlockSchema(block.Name), Block(block));
        }
    }

    public IFieldType? FieldType(SchemaField field) => schema.FieldTypes.Find(field.FieldType);

    private JsonObject Entry(SchemaType type)
    {
        var description = type.Kind == SchemaTypeKind.Singleton ? $"The {type.Name} singleton." : $"An entry of {type.Name}.";
        JsonObject properties = new()
        {
            ["id"] = System(new JsonObject { ["type"] = "string", ["format"] = "uuid" }),
            ["status"] = System(SharedComponents.Enum<EntryStatus>()),
            ["locale"] = System(new JsonObject { ["type"] = "string", ["description"] = "The locale of the entry, empty for a type that is not localized." }),
            ["version"] = System(new JsonObject { ["type"] = "integer", ["format"] = "int32", ["description"] = "The version of this locale, sent back as If-Match." }),
            ["createdAt"] = System(new JsonObject { ["type"] = "string", ["format"] = "date-time" }),
            ["createdBy"] = System(new JsonObject { ["type"] = "string", ["format"] = "uuid", ["description"] = "Only when fields= asks for it." }),
            ["updatedAt"] = System(new JsonObject { ["type"] = "string", ["format"] = "date-time" }),
            ["updatedBy"] = System(new JsonObject { ["type"] = "string", ["format"] = "uuid", ["description"] = "Only when fields= asks for it." }),
            ["publishedAt"] = System(new JsonObject { ["type"] = new JsonArray("string", "null"), ["format"] = "date-time" }),
            ["fallbacks"] = System(new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                ["description"] = "The locale each field was read from when the fallback chain filled it.",
            }),
        };

        foreach (var field in type.Fields.Where(field => !field.Hidden))
        {
            properties[field.Name] = Field(field);
        }

        return WithExample(new JsonObject { ["type"] = "object", ["description"] = description, ["properties"] = properties }, type);
    }

    // What a create, a replace or a merge patch sends: the fields without the system ones, a relation as ids and rich text
    // as a document. A read-only field may be sent back as it was read.
    private JsonObject Input(SchemaType type)
    {
        JsonObject properties = [];
        foreach (var field in type.Fields.Where(field => !field.Hidden))
        {
            properties[field.Name] = Field(field, read: false);
        }

        return new JsonObject { ["type"] = "object", ["description"] = $"The fields of {type.Name} as a write sends them.", ["properties"] = properties };
    }

    private JsonObject Block(SchemaType block)
    {
        var json = Fields(block, $"The {block.Name} block.");
        var properties = (JsonObject)json["properties"]!;
        JsonObject ordered = new()
        {
            [BlockDiscriminator] = new JsonObject { ["type"] = "string", ["const"] = block.Name },
            [BlockId] = new JsonObject
            {
                ["type"] = "string",
                ["pattern"] = RichTextAttributes.BlockIdRule,
                ["description"] = "The id of the block within its top-level field. A write that leaves it out gets one.",
            },
        };
        foreach (var (name, value) in properties.ToList())
        {
            properties.Remove(name);
            ordered[name] = value;
        }

        json["properties"] = ordered;
        json["required"] = new JsonArray(BlockDiscriminator);
        return json;
    }

    private JsonObject Fields(SchemaType owner, string description)
    {
        JsonObject properties = [];
        foreach (var field in owner.Fields.Where(field => !field.Hidden))
        {
            properties[field.Name] = Field(field);
        }

        return WithExample(new JsonObject { ["type"] = "object", ["description"] = description, ["properties"] = properties }, owner);
    }

    // The field type's fragment, widened on a read where a query changes the value: populate turns a relation into the
    // entry, and richText=html or markdown turns a document into a string. Nullability belongs to the object that holds it.
    private JsonObject Field(SchemaField field, bool read = true)
    {
        var fragment = FieldType(field)?.OpenApiSchema(field) ?? [];
        fragment = !read ? fragment : field.FieldType switch
        {
            FieldTypeNames.Relation when field.Relation is { } relation && schema.FindContentType(relation.Target) is not null =>
                Populated(fragment, relation.Target, field.Multiple),
            FieldTypeNames.RichText => new JsonObject
            {
                ["anyOf"] = new JsonArray(fragment, new JsonObject { ["type"] = "string", ["description"] = "HTML or Markdown, when richText= asks for it." }),
            },
            _ => fragment,
        };

        if (Nullable(field))
        {
            fragment = OrNull(fragment);
        }

        if (field.ReadOnly)
        {
            fragment = Annotate(fragment, "readOnly", true);
        }

        if (field.Display?.Help is { Length: > 0 } help)
        {
            fragment["description"] = help;
        }

        return fragment;
    }

    private static JsonObject Populated(JsonObject fragment, string target, bool multiple)
    {
        var entry = new JsonObject { ["$ref"] = OpenApiNames.EntryRef(target) };
        if (multiple && fragment["items"] is JsonObject id)
        {
            fragment["items"] = new JsonObject { ["anyOf"] = new JsonArray(id.DeepClone(), entry) };
            return fragment;
        }

        return new JsonObject { ["anyOf"] = new JsonArray(fragment, entry) };
    }

    // Spec 4.3: lists are never null, nor are rich text, slugs and required text, which store an empty value instead.
    private static bool Nullable(SchemaField field) =>
        !field.Multiple
        && field.FieldType is not (FieldTypeNames.RichText or FieldTypeNames.Slug or FieldTypeNames.Blocks)
        && !(field.Required && field.FieldType is FieldTypeNames.Text or FieldTypeNames.LongText);

    private static JsonObject OrNull(JsonObject fragment)
    {
        if (!fragment.ContainsKey("$ref") && fragment["type"] is JsonValue type && type.TryGetValue<string>(out var name))
        {
            fragment["type"] = new JsonArray(name, "null");
            if (fragment["enum"] is JsonArray values)
            {
                values.Add(null);
            }

            return fragment;
        }

        if (fragment.Count == 1 && fragment["anyOf"] is JsonArray choices)
        {
            choices.Add(new JsonObject { ["type"] = "null" });
            return fragment;
        }

        return fragment.Count == 0 ? fragment : new JsonObject { ["anyOf"] = new JsonArray(fragment, new JsonObject { ["type"] = "null" }) };
    }

    // A $ref keeps only description beside it once read, so other keywords go on an allOf around it.
    private static JsonObject Annotate(JsonObject fragment, string keyword, JsonNode value)
    {
        if (fragment.ContainsKey("$ref"))
        {
            fragment = new JsonObject { ["allOf"] = new JsonArray(fragment) };
        }

        fragment[keyword] = value;
        return fragment;
    }

    private static JsonObject System(JsonObject property)
    {
        property["readOnly"] = true;
        return property;
    }

    private JsonObject WithExample(JsonObject json, SchemaType type)
    {
        if (type.ClrType is { } clrType && options.Examples.TryGetValue(clrType, out var example))
        {
            json["examples"] = new JsonArray(example.DeepClone());
        }

        return json;
    }
}
