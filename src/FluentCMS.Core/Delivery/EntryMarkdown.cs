using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Delivery;

// The Markdown representation of a published entry (API-03). Rich text is stored as Markdown (SCH-02), so it goes in as written;
// every other value is written as text that reads as written, so no value can pose as a heading, a field, a source or a link.
// Fields follow in the order of the definition the published version was written against.
public static class EntryMarkdown
{
    public const string MediaType = "text/markdown";

    // Markdown lines end in \n whatever the platform, JSON blocks included.
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n" };

    // The first text field that holds text, read from its fact on an entity, on one line; null when there is none.
    public static string? Title(JsonElement fields, ContentTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return TitleField(fields, definition)?.Text;
    }

    // Plain text as Markdown that reads as written (CommonMark lets a backslash escape any punctuation). The characters that open
    // emphasis, code, links, HTML, entities, headings and tables are escaped wherever they are, and those that open a list, a quote
    // or a heading underline where a line starts. Lines lose their indentation, so none becomes a code block; breaks stay.
    public static string Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var escaped = new StringBuilder(text.Length + 16);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                escaped.Append('\n');
            }

            var line = lines[index].TrimStart();
            var digits = line.TakeWhile(char.IsAsciiDigit).Count();
            for (var position = 0; position < line.Length; position++)
            {
                var character = line[position];
                var opensLine = position == 0 && character is '-' or '+' or '=' or '>';
                var endsListNumber = position == digits && digits > 0 && character is '.' or ')';
                if (character is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '&' or '#' or '|' or '~' || opensLine || endsListNumber)
                {
                    escaped.Append('\\');
                }

                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }

    // Text for one line, such as a heading or a link's text: its whitespace collapsed, then escaped as Text is.
    public static string InlineText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Text(Line(text));
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
            .Append(CultureInfo.InvariantCulture, $"id: \"{item.Id}\"\n")
            .Append(CultureInfo.InvariantCulture, $"type: \"{item.ContentType}\"\n")
            .Append(CultureInfo.InvariantCulture, $"version: {item.Version}\n")
            .Append(CultureInfo.InvariantCulture, $"url: \"{url}\"\n")
            .Append("---\n\n")
            .Append(CultureInfo.InvariantCulture, $"# {InlineText(title?.Text ?? $"{item.ContentTypeName} {item.Id}")}\n");

        // The heading shows the title, so its field is not repeated below it.
        foreach (var field in definition.Fields.Where(field => field.Name != title?.Field))
        {
            if (Value(item.Fields, field.Name, definition.IsEntity) is not { } value)
            {
                continue;
            }

            markdown.Append(CultureInfo.InvariantCulture, $"\n## {field.Name}\n\n").Append(Block(field.Type, field.Settings, value, entryUrl)).Append('\n');

            // A list item with a strong label, which an escaped value cannot form, so a value cannot pass for a source.
            if (definition.IsEntity)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"\n- **Source:** {Source(item.Fields, field.Name) ?? "none, so this fact is unsourced"}\n");
            }
        }

        if (item.Unresolved.Count > 0)
        {
            markdown.Append("\n## Unresolved references\n\n");
            foreach (var reference in item.Unresolved)
            {
                markdown.Append(CultureInfo.InvariantCulture, $"- `{reference.Path}`: {reference.Field} of entry {reference.EntityId} {Reason(reference.Reason)}\n");
            }
        }

        return markdown.ToString();
    }

    private static (string Field, string Text)? TitleField(JsonElement fields, ContentTypeDefinition definition)
    {
        foreach (var field in definition.Fields.Where(field => field.Type == "text"))
        {
            if (Value(fields, field.Name, definition.IsEntity) is { ValueKind: JsonValueKind.String } value)
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
        if (!fields.TryGetProperty(name, out var fact)
            || fact.ValueKind != JsonValueKind.Object
            || !fact.TryGetProperty(FactMetadata.Source, out var source)
            || source.ValueKind != JsonValueKind.Object
            || !source.TryGetProperty(FactMetadata.SourceReference, out var reference)
            || reference.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = InlineText(reference.GetString()!);
        return source.TryGetProperty(FactMetadata.SourceKind, out var kind) && kind.ValueKind == JsonValueKind.String
            ? $"{text} \\({InlineText(kind.GetString()!)}\\)"
            : text;
    }

    private static string Reason(UnresolvedReason reason)
    {
        return reason switch
        {
            UnresolvedReason.NotPublished => "is not published",
            UnresolvedReason.NoValue => "has no value",
            UnresolvedReason.Deleted => "is deleted",
            _ => "cannot be resolved",
        };
    }

    private static string Block(string type, JsonElement settings, JsonElement value, Func<Guid, string> entryUrl)
    {
        if (type == "list" && value.ValueKind == JsonValueKind.Array)
        {
            var itemType = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("itemType", out var item) && item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : "";
            var itemSettings = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("itemSettings", out var nested) ? nested : FieldDefinition.EmptySettings;

            // Each item keeps its own block, indented under its bullet so paragraphs and fences stay inside the item.
            return string.Join('\n', value.EnumerateArray()
                .Where(element => element.ValueKind != JsonValueKind.Null)
                .Select(element => "- " + Block(itemType, itemSettings, element, entryUrl).Replace("\n", "\n  ", StringComparison.Ordinal).Replace("\n  \n", "\n\n", StringComparison.Ordinal)));
        }

        // A reference to an entity field already holds the resolved value; a reference to a whole entry holds its id.
        var wholeEntry = type == "reference" && !(settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("field", out _));

        return value.ValueKind switch
        {
            JsonValueKind.String when type == "richText" => value.GetString()!.Trim(),
            JsonValueKind.String when wholeEntry && value.TryGetGuid(out var id) => $"[{id}]({entryUrl(id)})",
            JsonValueKind.String when type == "media" && value.TryGetGuid(out var asset) => $"Media asset `{asset}`",
            JsonValueKind.String => Text(value.GetString()!),
            JsonValueKind.Object or JsonValueKind.Array => Fenced(value),
            _ when type == "json" => Fenced(value),
            _ => value.GetRawText(),
        };
    }

    // The serializer writes a backtick in a string as `, so no value inside can close the fence.
    private static string Fenced(JsonElement value)
    {
        return $"```json\n{JsonSerializer.Serialize(value, Indented)}\n```";
    }

    private static string Line(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
