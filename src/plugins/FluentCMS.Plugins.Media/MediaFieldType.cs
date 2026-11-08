using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.Media;

// One asset (MediaRef) or an ordered list of them (MediaRefList). Core stores a field of it in cms_reference with the
// target media, so it filters by asset id, and a read gives { "id", "type": "media" }, or the asset when populated. A
// single field coerces to a Guid, or null for an empty one, and a list to a Guid[] in order, as a relation does.
internal sealed class MediaFieldType : IFieldType
{
    public const string TypeName = "media";

    private const string IdKey = "id";

    public string Name => TypeName;

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(MediaRef), typeof(MediaRefList)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator> { FilterOperator.Equal, FilterOperator.In };

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [];

    public bool SupportsMultiple => true;

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Reference;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        ArgumentNullException.ThrowIfNull(field);

        result = null;
        message = null;
        var raw = Unwrap(value);
        if (raw is null)
        {
            return true;
        }

        if (!field.Multiple)
        {
            if (!TryReadId(raw, out var id))
            {
                message = "is not a media id: expected a GUID";
                return false;
            }

            result = id == Guid.Empty ? null : id;
            return true;
        }

        IEnumerable<object?> items;
        switch (raw)
        {
            case MediaRefList list:
                items = list.Select(item => (object?)item);
                break;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                items = [.. array.EnumerateArray().Select(item => Unwrap(item))];
                break;
            case string or JsonElement or IEnumerable<KeyValuePair<string, object?>>:
                message = "is not a list of media ids";
                return false;
            case IEnumerable list:
                items = list.Cast<object?>();
                break;
            default:
                message = "is not a list of media ids";
                return false;
        }

        List<Guid> ids = [];
        foreach (var item in items)
        {
            if (!TryReadId(Unwrap(item), out var id) || id == Guid.Empty)
            {
                message = $"item {ids.Count} is not a media id: expected a GUID";
                return false;
            }

            ids.Add(id);
        }

        result = ids.ToArray();
        return true;
    }

    // What a write sends is an id, or what a read gave, which is the reference or the populated asset.
    public JsonObject OpenApiSchema(SchemaField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var one = new JsonObject
        {
            ["anyOf"] = new JsonArray(
                new JsonObject { ["type"] = "string", ["format"] = "uuid", ["description"] = "The id of the asset, as a write sends it." },
                Reference(),
                Populated()),
        };
        return field.Multiple ? new JsonObject { ["type"] = "array", ["items"] = one } : one;
    }

    private static JsonObject Reference() => new()
    {
        ["type"] = "object",
        ["description"] = "The asset as a read gives it without populate, or beyond the populate depth.",
        ["properties"] = new JsonObject
        {
            [IdKey] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            ["type"] = new JsonObject { ["type"] = "string", ["const"] = TypeName },
        },
        ["required"] = new JsonArray(IdKey, "type"),
    };

    private static JsonObject Populated()
    {
        static JsonObject Text(string description) => new() { ["type"] = new JsonArray("string", "null"), ["description"] = description };

        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "The asset as populate gives it, with its alternative text and caption in the locale of the read. An asset the caller cannot read is null.",
            ["properties"] = new JsonObject
            {
                [IdKey] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                ["type"] = new JsonObject { ["type"] = "string", ["const"] = TypeName },
                ["fileName"] = new JsonObject { ["type"] = "string" },
                ["mimeType"] = new JsonObject { ["type"] = "string" },
                ["size"] = new JsonObject { ["type"] = "integer", ["format"] = "int64" },
                ["width"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                ["height"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                ["title"] = Text("The title."),
                ["alt"] = Text("The alternative text."),
                ["caption"] = Text("The caption."),
                ["credit"] = Text("Who to credit."),
                ["focalPoint"] = new JsonObject
                {
                    ["type"] = new JsonArray("object", "null"),
                    ["properties"] = new JsonObject
                    {
                        ["x"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                        ["y"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                    },
                },
                ["url"] = Text("The signed URL of the file, relative to the base of the CMS; null for a private asset."),
                ["presets"] = new JsonObject
                {
                    ["type"] = "object",
                    ["description"] = "The signed URL of each image preset of the site, by name.",
                    ["additionalProperties"] = new JsonObject { ["type"] = "string" },
                },
            },
            ["required"] = new JsonArray(IdKey, "type"),
        };
    }

    // A JsonNode or a JsonElement becomes null, a string or a JsonElement of an object or an array.
    private static object? Unwrap(object? value)
    {
        if (value is JsonNode node)
        {
            try
            {
                value = JsonSerializer.SerializeToElement(node);
            }
            catch (JsonException)
            {
                // Deeper than the serializer reads, so no id either way.
                return node;
            }
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement { ValueKind: JsonValueKind.String } text => text.GetString(),
            _ => value,
        };
    }

    // An id, a MediaRef, or the reference or populated asset a read gave, so that what was read can be written back.
    private static bool TryReadId(object? value, out Guid id)
    {
        switch (value)
        {
            case Guid guid:
                id = guid;
                return true;
            case MediaRef media:
                id = media.Id;
                return true;
            case string text:
                return Guid.TryParseExact(text, "D", out id);
            case JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty(IdKey, out var inner):
                return TryReadId(Unwrap(inner), out id);
            case IEnumerable<KeyValuePair<string, object?>> map when map.FirstOrDefault(pair => pair.Key == IdKey) is { Key: not null, Value: var inner }:
                return TryReadId(Unwrap(inner), out id);
            default:
                id = Guid.Empty;
                return false;
        }
    }
}
