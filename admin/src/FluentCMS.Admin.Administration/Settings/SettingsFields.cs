using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Settings;

// A property of an area's schema as the generated page edits it: the field the editors of 4.6 take, or a secret, which is
// marked writeOnly and never has a value in a read.
internal sealed record SettingsProperty(SchemaFieldDescription Field, bool Secret);

// The JSON schema of an area's read, as System.Text.Json writes it with the validation attributes added, read into the
// field descriptions of the content form. A property of a shape no core editor takes, such as an object, a dictionary or a
// list of free values, is edited as JSON.
internal static class SettingsFields
{
    public static IReadOnlyList<SettingsProperty> Of(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema["properties"] is not JsonObject properties)
        {
            return [];
        }

        var required = schema["required"] is JsonArray names
            ? names.Select(Text).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];
        List<SettingsProperty> fields = [];
        foreach (var (name, node) in properties)
        {
            var property = node as JsonObject ?? [];
            var (type, multiple, settings) = TypeOf(property);
            var field = new SchemaFieldDescription
            {
                Name = name,
                Type = type,
                Multiple = multiple,
                Display = Text(property["description"]) is { Length: > 0 } help ? new FieldDisplayDescription { Help = help } : null,
                Validation = new FieldValidationDescription
                {
                    Required = required.Contains(name),
                    MinLength = Integer(property["minLength"]),
                    MaxLength = Integer(property["maxLength"]),
                    Minimum = Number(property["minimum"]),
                    Maximum = Number(property["maximum"]),
                    Pattern = Text(property["pattern"]),
                },
                Settings = settings,
            };
            fields.Add(new SettingsProperty(field, property["writeOnly"] is JsonValue writeOnly && writeOnly.TryGetValue<bool>(out var secret) && secret));
        }

        return fields;
    }

    private static (string Type, bool Multiple, JsonObject Settings) TypeOf(JsonObject property)
    {
        if (Options(property) is { } options)
        {
            return ("select", false, new JsonObject { ["options"] = options });
        }

        var types = Types(property);
        if (types.Count != 1)
        {
            return ("json", false, []);
        }

        return types[0] switch
        {
            "boolean" => ("boolean", false, []),
            "integer" => ("integer", false, []),
            "number" => ("decimal", false, []),
            "string" => (Text(property["format"]) switch
            {
                "date-time" => "dateTime",
                "date" => "date",
                "email" => "email",
                "uri" => "url",
                _ => "text",
            }, false, []),
            "array" when property["items"] is JsonObject items && Options(items) is { } choices => ("select", true, new JsonObject { ["options"] = choices }),
            _ => ("json", false, []),
        };
    }

    // The values of an enumeration of texts, null left out, as a select offers them.
    private static JsonArray? Options(JsonObject property) =>
        property["enum"] is JsonArray values && values.Where(value => value is not null).ToList() is { Count: > 0 } named && named.All(value => Text(value) is not null)
            ? new JsonArray([.. named.Select(value => (JsonNode)Text(value)!)])
            : null;

    // The types of a property but null: "integer" and ["integer", "null"] are both an integer.
    private static List<string> Types(JsonObject property) => property["type"] switch
    {
        JsonArray many => [.. many.Select(Text).OfType<string>().Where(type => type != "null")],
        JsonValue one when Text(one) is { } type && type != "null" => [type],
        _ => [],
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Integer(JsonNode? node) =>
        node is JsonValue { } value && value.GetValueKind() == JsonValueKind.Number
            && int.TryParse(value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    // A bound such as long.MaxValue, which a double rounds, is read from its text.
    private static decimal? Number(JsonNode? node) =>
        node is JsonValue { } value && value.GetValueKind() == JsonValueKind.Number
            && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
