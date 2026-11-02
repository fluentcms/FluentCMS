using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// Checks that the value is a document: a JSON object of type doc whose content, if any, is an array. Nodes and
// marks are not validated here; the rich text parser does that when the document is read.
internal sealed class RichTextFieldType()
    : CoreFieldType(FieldTypeNames.RichText, [typeof(RichText)], FilterSets.NullChecks, sortable: false, indexable: false)
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 128 };

    public override FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Json;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        string json;
        switch (raw)
        {
            case null:
                return Ok(null, out result, out message);
            case RichText document:
                json = document.Json;
                break;
            case string text:
                json = text;
                break;
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                json = element.GetRawText();
                break;
            default:
                return Expected("a rich text document", raw, out result, out message);
        }

        return Check(json) is { } problem ? Fail(problem, out result, out message) : Ok(raw as RichText ?? new RichText(json), out result, out message);
    }

    public override JsonObject OpenApiSchema(SchemaField field) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["type"] = new JsonObject { ["const"] = "doc" },
            ["content"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } },
        },
        ["required"] = new JsonArray("type"),
    };

    private static string? Check(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, Options);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "doc")
            {
                return "is not a rich text document: the root must be an object of type doc";
            }

            if (root.TryGetProperty("content", out var content) && content.ValueKind != JsonValueKind.Array)
            {
                return "is not a rich text document: content must be an array";
            }

            return null;
        }
        catch (JsonException exception)
        {
            return $"is not valid JSON: {exception.Message}";
        }
    }
}
