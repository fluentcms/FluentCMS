using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Content;

namespace FluentCMS.Plugins.Mcp;

// The arguments of a tool call, which the server does not check against the input schema. A wrong one is a validation
// error on the name of the argument, which the server answers as the REST APIs answer a wrong body.
internal static class McpArguments
{
    // An entry may nest as deep as core stores one, inside the object of a list or a publish result.
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 256, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static McpToolResult Result(JsonNode node) => McpToolResult.FromJson(JsonSerializer.SerializeToElement(node, Json));

    public static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node, Json);

    // The bytes a result takes as it is sent.
    public static int Size(JsonNode node) => JsonSerializer.SerializeToUtf8Bytes(node, Json).Length;

    // A key the tool does not take is refused rather than ignored, as a body of the Management API is.
    public static void Only(JsonElement arguments, params ReadOnlySpan<string> names)
    {
        List<ValidationError> errors = [];
        foreach (var property in arguments.EnumerateObject())
        {
            if (!names.Contains(property.Name))
            {
                errors.Add(new ValidationError(property.Name, ValidationErrorCodes.InvalidValue, $"'{property.Name}' is not an argument of this tool."));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    public static Guid Id(JsonElement arguments, string name = "id") =>
        OptionalId(arguments, name) ?? throw Required(name);

    public static Guid? OptionalId(JsonElement arguments, string name)
    {
        if (!TryGet(arguments, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out var id)
            ? id
            : throw Invalid(name, $"'{name}' is an id, such as 0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f.");
    }

    public static string RequiredText(JsonElement arguments, string name) =>
        Text(arguments, name) is { Length: > 0 } text ? text : throw Required(name);

    public static string? Text(JsonElement arguments, string name)
    {
        if (!TryGet(arguments, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid(name, $"'{name}' is a string.");
    }

    public static int? Integer(JsonElement arguments, string name)
    {
        if (!TryGet(arguments, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw Invalid(name, $"'{name}' is a whole number.");
    }

    public static bool? Flag(JsonElement arguments, string name)
    {
        if (!TryGet(arguments, name, out var value))
        {
            return null;
        }

        return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw Invalid(name, $"'{name}' is true or false.");
    }

    public static IReadOnlyList<string>? Texts(JsonElement arguments, string name)
    {
        if (!TryGet(arguments, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
            ? [.. value.EnumerateArray().Select(item => item.GetString()!)]
            : throw Invalid(name, $"'{name}' is an array of strings.");
    }

    public static JsonElement? Object(JsonElement arguments, string name)
    {
        if (!TryGet(arguments, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object ? value : throw Invalid(name, $"'{name}' is an object.");
    }

    // The values stay JSON, as a body of the Management API does: the engine reads them against the fields of the type.
    public static ContentDocument Document(JsonElement fields)
    {
        ContentDocument entry = [];
        foreach (var property in fields.EnumerateObject())
        {
            entry[property.Name] = property.Value;
        }

        return entry;
    }

    public static ValidationException Required(string name) =>
        new([new ValidationError(name, ValidationErrorCodes.Required, $"'{name}' is required.")]);

    public static ValidationException Invalid(string name, string message) =>
        new([new ValidationError(name, ValidationErrorCodes.InvalidValue, message)]);

    // An argument that is null counts as left out.
    private static bool TryGet(JsonElement arguments, string name, out JsonElement value) =>
        arguments.TryGetProperty(name, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
}

// The query arguments of a read tool as the parameters of a list of the REST APIs, which core's parser turns into the
// query, so that a tool and a route build the same one: { "filter": { "title": { "eq": "A" } } } is filter[title][eq]=A,
// the items of or and and take their index, and the values of in, nin and between are joined by commas.
internal static class McpQueryArguments
{
    public const string Filter = "filter";
    public const string Search = "q";

    public static readonly string[] List = [Filter, "sort", "page", "pageSize", "cursor", "fields", "populate", "depth", "status", "locale", "fallback", "richText", "withTotal"];

    public static readonly string[] Single = ["fields", "populate", "depth", "status", "locale", "fallback", "richText"];

    // The arguments named, in the order sent. An argument that is null is left out.
    public static List<KeyValuePair<string, string>> Parameters(JsonElement arguments, IReadOnlyCollection<string> names)
    {
        List<KeyValuePair<string, string>> parameters = [];
        foreach (var property in arguments.EnumerateObject())
        {
            if (!names.Contains(property.Name) || property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            if (property.Name == Filter)
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    throw McpArguments.Invalid(Filter, "'filter' is an object of conditions, such as { \"title\": { \"eq\": \"Hello\" } }.");
                }

                Flatten(Filter, property.Value, parameters);
            }
            else
            {
                parameters.Add(KeyValuePair.Create(property.Name, property.Value.ValueKind == JsonValueKind.Array ? Joined(property.Name, property.Value, escape: false) : Scalar(property.Name, property.Value)));
            }
        }

        return parameters;
    }

    private static void Flatten(string key, JsonElement value, List<KeyValuePair<string, string>> parameters)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    Flatten($"{key}[{property.Name}]", property.Value, parameters);
                }

                break;
            case JsonValueKind.Array when value.GetArrayLength() > 0 && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object):
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    Flatten(string.Create(CultureInfo.InvariantCulture, $"{key}[{index++}]"), item, parameters);
                }

                break;
            case JsonValueKind.Array:
                parameters.Add(KeyValuePair.Create(key, Joined(key, value, escape: true)));
                break;
            default:
                parameters.Add(KeyValuePair.Create(key, Scalar(key, value)));
                break;
        }
    }

    // A list operand of the grammar: a comma between the values, and \, and \\ for a comma and a backslash in one.
    private static string Joined(string key, JsonElement items, bool escape)
    {
        StringBuilder joined = new();
        var first = true;
        foreach (var item in items.EnumerateArray())
        {
            if (!first)
            {
                joined.Append(',');
            }

            first = false;
            var text = Scalar(key, item);
            joined.Append(escape ? text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal) : text);
        }

        return joined.ToString();
    }

    private static string Scalar(string key, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => throw McpArguments.Invalid(key, $"'{key}' takes a string, a number or true or false, or an array of them."),
    };
}
