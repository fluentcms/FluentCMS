using System.Text.Json.Nodes;

namespace FluentCMS.Core.Schema;

// The schema as agents read it (SCH-06): a JSON Schema (draft 2020-12) for the entries of a content type, with a description and
// an example for every field and one example entry. On an entity type every field is a fact with its metadata (SCH-03).
public static class SchemaDocument
{
    public const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    private const string FactSentence = "It is a fact: give its value with its source, owner and validity.";
    private const string ExampleTime = "2026-01-01T00:00:00Z";

    public static JsonObject ForType(ContentType type, FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        var document = Describe(type, fieldTypes);
        document.Insert(0, "$schema", Dialect);
        return document;
    }

    // Every type of the site under $defs, keyed by slug.
    public static JsonObject ForSite(IEnumerable<ContentType> types, FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fieldTypes);

        var definitions = new JsonObject();
        foreach (var type in types.OrderBy(type => type.Slug, StringComparer.Ordinal))
        {
            definitions[type.Slug] = Describe(type, fieldTypes);
        }

        return new JsonObject
        {
            ["$schema"] = Dialect,
            ["$id"] = "urn:fluentcms:schema",
            ["title"] = "Content types",
            ["description"] = "Every content type of the site. An entry of a type follows the schema under $defs named by the type's slug.",
            ["$defs"] = definitions,
        };
    }

    private static JsonObject Describe(ContentType type, FieldTypeRegistry fieldTypes)
    {
        var isEntity = type.Definition.IsEntity;
        var properties = new JsonObject();
        var required = new JsonArray();
        var example = new JsonObject();

        foreach (var field in type.Definition.Fields)
        {
            var value = fieldTypes.DescribeValue(field.Type, field.Settings);
            var schema = isEntity ? Fact(value.Schema) : value.Schema;
            var fieldExample = isEntity ? FactExample(value.Example) : value.Example;

            schema["description"] = Sentences(field.Description, value.Description, isEntity ? FactSentence : null);
            schema["examples"] = new JsonArray(fieldExample?.DeepClone());
            properties[field.Name] = schema;
            example[field.Name] = fieldExample;

            if (field.Required)
            {
                required.Add(field.Name);
            }
        }

        return new JsonObject
        {
            ["$id"] = $"urn:fluentcms:content-type:{type.Slug}:{type.Version}",
            ["title"] = type.Name,
            ["description"] = Sentences(
                type.Definition.Description,
                isEntity
                    ? $"An entity of the type '{type.Slug}', a source of facts: every field carries its source, owner and validity."
                    : $"An entry of the content type '{type.Slug}'."),
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
            ["examples"] = new JsonArray(example),
            ["x-fluentcms"] = new JsonObject { ["slug"] = type.Slug, ["version"] = type.Version, ["entity"] = isEntity },
        };
    }

    // Everything but the value may be null, so an agent states that a fact has no source yet instead of leaving it out (ENT-02).
    private static JsonObject Fact(JsonObject value)
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                [FactMetadata.Value] = value,
                [FactMetadata.Source] = new JsonObject
                {
                    ["type"] = Strings(["object", "null"]),
                    ["description"] = "Where the value comes from, or null when it has none yet, which flags the fact as unsourced.",
                    ["properties"] = new JsonObject
                    {
                        [FactMetadata.SourceKind] = new JsonObject { ["enum"] = Strings(FactMetadata.SourceKinds) },
                        [FactMetadata.SourceReference] = new JsonObject
                        {
                            ["type"] = "string",
                            ["minLength"] = 1,
                            ["description"] = "The URL, file, person or system the value comes from.",
                        },
                    },
                    ["required"] = Strings([FactMetadata.SourceKind, FactMetadata.SourceReference]),
                    ["additionalProperties"] = false,
                },
                [FactMetadata.Owner] = Nullable("uuid", "The id of the user who owns the fact."),
                [FactMetadata.ValidFrom] = Nullable("date-time", "When the value starts to hold."),
                [FactMetadata.ValidUntil] = Nullable("date-time", "When the value stops holding."),
                [FactMetadata.LastVerified] = Nullable("date-time", "When someone last confirmed the value."),
            },
            ["required"] = Strings([FactMetadata.Value, .. FactMetadata.Properties]),
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject FactExample(JsonNode? value)
    {
        return new JsonObject
        {
            [FactMetadata.Value] = value,
            [FactMetadata.Source] = new JsonObject
            {
                [FactMetadata.SourceKind] = "url",
                [FactMetadata.SourceReference] = "https://example.com/source",
            },
            [FactMetadata.Owner] = FieldValueDescription.ExampleId,
            [FactMetadata.ValidFrom] = ExampleTime,
            [FactMetadata.ValidUntil] = null,
            [FactMetadata.LastVerified] = ExampleTime,
        };
    }

    private static JsonObject Nullable(string format, string description)
    {
        return new JsonObject { ["type"] = Strings(["string", "null"]), ["format"] = format, ["description"] = description };
    }

    private static JsonArray Strings(IEnumerable<string> values)
    {
        return new JsonArray([.. values.Select(value => (JsonNode?)value)]);
    }

    private static string Sentences(params string?[] sentences)
    {
        return string.Join(" ", sentences.Where(sentence => !string.IsNullOrWhiteSpace(sentence)));
    }
}
