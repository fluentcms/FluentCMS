using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Delivery;

// The Markdown representation of a published entry (API-03). Rich text is stored as Markdown (SCH-02), so it goes in as written;
// every other value is written out by its field's type, in the order the content type defines its fields.
public static class EntryMarkdown
{
    public const string MediaType = "text/markdown";

    // Markdown lines end in \n whatever the platform, JSON blocks included.
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    // The first text field that holds text, read from its fact on an entity; null when there is none.
    public static string? Title(JsonElement fields, ContentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return TitleField(fields, definition)?.Text;
    }

    // EntryUrl gives the Markdown URL of another entry, for references to whole entries.
    public static string Render(PublishedItem item, string url, Func<Guid, string> entryUrl)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(entryUrl);

        var definition = item.Definition;
        var title = TitleField(item.Fields, definition);
        var markdown = new StringBuilder()
            .Append("---\n")
            .Append(CultureInfo.InvariantCulture, $"id: {item.Id}\n")
            .Append(CultureInfo.InvariantCulture, $"type: {item.ContentType}\n")
            .Append(CultureInfo.InvariantCulture, $"version: {item.Version}\n")
            .Append(CultureInfo.InvariantCulture, $"url: {url}\n")
            .Append("---\n\n")
            .Append(CultureInfo.InvariantCulture, $"# {title?.Text ?? $"{item.ContentTypeName} {item.Id}"}\n");

        // The heading shows the title, so its field is not repeated below it.
        foreach (var field in definition.Fields.Where(field => field.Name != title?.Field))
        {
            if (Value(item.Fields, field.Name, definition.IsEntity) is not { } value)
            {
                continue;
            }

            markdown.Append(CultureInfo.InvariantCulture, $"\n## {field.Name}\n\n").Append(Block(field.Type, field.Settings, value, entryUrl)).Append('\n');

            if (definition.IsEntity && Source(item.Fields, field.Name) is { } source)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"\nSource: {source}\n");
            }
        }

        return markdown.ToString();
    }

    private static (string Field, string Text)? TitleField(JsonElement fields, ContentTypeDefinition definition)
    {
        foreach (var field in definition.Fields.Where(field => field.Type == "text"))
        {
            if (Value(fields, field.Name, definition.IsEntity) is { ValueKind: JsonValueKind.String } value
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return (field.Name, Line(value.GetString()!));
            }
        }

        return null;
    }

    // The field's value: on an entity, the value inside its fact. Null, blank and missing values are left out.
    private static JsonElement? Value(JsonElement fields, string name, bool isEntity)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (isEntity && value.ValueKind == JsonValueKind.Object && !value.TryGetProperty(FactMetadata.Value, out value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Null || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
            ? null
            : value;
    }

    private static string? Source(JsonElement fields, string name)
    {
        return fields.TryGetProperty(name, out var fact)
            && fact.ValueKind == JsonValueKind.Object
            && fact.TryGetProperty(FactMetadata.Source, out var source)
            && source.ValueKind == JsonValueKind.Object
            && source.TryGetProperty(FactMetadata.SourceReference, out var reference)
            && reference.ValueKind == JsonValueKind.String
                ? source.TryGetProperty(FactMetadata.SourceKind, out var kind) && kind.ValueKind == JsonValueKind.String
                    ? $"{reference.GetString()} ({kind.GetString()})"
                    : reference.GetString()
                : null;
    }

    private static string Block(string type, JsonElement settings, JsonElement value, Func<Guid, string> entryUrl)
    {
        if (type == "list" && value.ValueKind == JsonValueKind.Array)
        {
            var itemType = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("itemType", out var item) ? item.GetString() ?? "" : "";
            var itemSettings = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("itemSettings", out var nested) ? nested : FieldDefinition.EmptySettings;

            return string.Join('\n', value.EnumerateArray()
                .Where(element => element.ValueKind != JsonValueKind.Null)
                .Select(element => $"- {Line(Inline(itemType, itemSettings, element, entryUrl))}"));
        }

        return type switch
        {
            "richText" when value.ValueKind == JsonValueKind.String => value.GetString()!.Trim(),
            "json" => Fenced(value),
            _ when value.ValueKind is JsonValueKind.Object or JsonValueKind.Array => Fenced(value),
            _ => Inline(type, settings, value, entryUrl),
        };
    }

    private static string Inline(string type, JsonElement settings, JsonElement value, Func<Guid, string> entryUrl)
    {
        // A reference to an entity field already holds the resolved value; a reference to a whole entry holds its id.
        var wholeEntry = type == "reference" && !(settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("field", out _));

        return value.ValueKind switch
        {
            JsonValueKind.String when wholeEntry && value.TryGetGuid(out var id) => $"[{id}]({entryUrl(id)})",
            JsonValueKind.String when type == "media" => $"Media asset `{value.GetString()}`",
            JsonValueKind.String => value.GetString()!,
            _ => value.GetRawText(),
        };
    }

    private static string Fenced(JsonElement value)
    {
        return $"```json\n{JsonSerializer.Serialize(value, Indented)}\n```";
    }

    private static string Line(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
