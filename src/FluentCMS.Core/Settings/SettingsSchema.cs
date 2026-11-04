using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace FluentCMS.Core.Settings;

// The JSON schema of a settings class, for the admin to render a form from and for the Management endpoint to return. It is
// what System.Text.Json says of the type, plus what the validation attributes of its properties say. A default shown is the
// code default; a secret has none, and is marked write-only.
internal static class SettingsSchema
{
    public static JsonNode Create(SettingsDescriptor descriptor, IReadOnlyList<JsonPropertyInfo> properties)
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(
            SettingsJson.Strict,
            descriptor.Type,
            new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true, TransformSchemaNode = (context, node) => Transform(descriptor, context, node) });

        if (schema is JsonObject root)
        {
            root["title"] = descriptor.Area;
            AddDefaultsAndRequired(root, descriptor, properties);
        }

        return schema;
    }

    private static JsonNode Transform(SettingsDescriptor descriptor, JsonSchemaExporterContext context, JsonNode node)
    {
        if (node is not JsonObject schema || context.PropertyInfo is not { } property)
        {
            return node;
        }

        var attributes = property.AttributeProvider?.GetCustomAttributes(inherit: true) ?? [];
        var isText = property.PropertyType == typeof(string);
        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case DescriptionAttribute { Description.Length: > 0 } description:
                    schema["description"] = description.Description;
                    break;
                case RangeAttribute range when Number(range.Minimum) is { } minimum && Number(range.Maximum) is { } maximum:
                    schema["minimum"] = minimum;
                    schema["maximum"] = maximum;
                    break;
                case StringLengthAttribute length:
                    schema["maxLength"] = length.MaximumLength;
                    if (length.MinimumLength > 0)
                    {
                        schema["minLength"] = length.MinimumLength;
                    }

                    break;
                case MinLengthAttribute min:
                    schema[isText ? "minLength" : "minItems"] = min.Length;
                    break;
                case MaxLengthAttribute max:
                    schema[isText ? "maxLength" : "maxItems"] = max.Length;
                    break;
                case RegularExpressionAttribute pattern when isText:
                    schema["pattern"] = pattern.Pattern;
                    break;
                case EmailAddressAttribute when isText:
                    schema["format"] = "email";
                    break;
                case UrlAttribute when isText:
                    schema["format"] = "uri";
                    break;
                case RequiredAttribute when isText && !schema.ContainsKey("minLength"):
                    schema["minLength"] = 1;
                    break;
            }
        }

        if (descriptor.SecretKeys.Contains(property.Name))
        {
            schema["writeOnly"] = true;
        }

        return schema;
    }

    private static void AddDefaultsAndRequired(JsonObject root, SettingsDescriptor descriptor, IReadOnlyList<JsonPropertyInfo> properties)
    {
        if (root["properties"] is not JsonObject schemas)
        {
            return;
        }

        var defaults = descriptor.CreateDefault();
        JsonArray required = [];
        foreach (var property in properties)
        {
            if (schemas[property.Name] is not JsonObject schema)
            {
                continue;
            }

            if (!descriptor.SecretKeys.Contains(property.Name) && property.Get?.Invoke(defaults) is { } value)
            {
                schema["default"] = JsonSerializer.SerializeToNode(value, property.PropertyType, SettingsJson.Strict);
            }

            if (property.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: true) ?? false)
            {
                required.Add(property.Name);
            }
        }

        if (required.Count > 0 && root["required"] is null)
        {
            root["required"] = required;
        }
    }

    private static JsonNode? Number(object value) => value switch
    {
        int number => number,
        long number => number,
        double number => number,
        float number => number,
        decimal number => number,
        IConvertible convertible when value is not string => Convert.ToDouble(convertible, CultureInfo.InvariantCulture),
        _ => null,
    };
}
