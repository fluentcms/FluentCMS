using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Plugins.Search;

// What the index stores and what a search looks for, folded the same way for every provider: lower case, the accents of
// letters taken off, and words of letters and digits with one space between them. The tokenizers of SQLite and
// PostgreSQL, and a LIKE on SQL Server, then see the same words.
internal static class SearchText
{
    // The most characters of folded text one entry keeps; a longer text is cut at a word.
    public const int MaxLength = 100_000;

    // The fields of a type that the index reads. A hidden field is never in a response, so it is not searched either.
    public static IReadOnlyList<SchemaField> Fields(SchemaType type) => [.. type.Fields.Where(field => field.Searchable && !field.Hidden)];

    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var separated = false;
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in text.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (!Rune.IsLetterOrDigit(rune))
            {
                separated = builder.Length > 0;
                continue;
            }

            if (separated)
            {
                builder.Append(' ');
                separated = false;
            }

            builder.Append(buffer[..Rune.ToLowerInvariant(rune).EncodeToUtf16(buffer)]);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> Words(string text) => Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // The folded text of the fields that an entry read holds: text, the texts of a list, and rich text as plain text.
    // Other values, such as numbers, relations and components, have no text to search.
    public static string Of(ContentDocument entry, IEnumerable<SchemaField> fields)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder();
        foreach (var field in fields)
        {
            if (entry.TryGetValue(field.Name, out var value))
            {
                Append(builder, value);
            }
        }

        return Cut(Fold(builder.ToString()));
    }

    private static string Cut(string text)
    {
        if (text.Length <= MaxLength)
        {
            return text;
        }

        var end = text.LastIndexOf(' ', MaxLength);
        return text[..(end > 0 ? end : MaxLength)];
    }

    private static void Append(StringBuilder builder, object? value)
    {
        switch (value)
        {
            case string text:
                builder.Append(text).Append(' ');
                break;
            case RichText rich:
                AppendRichText(builder, rich.Json);
                break;
            case JsonValue json when json.TryGetValue<string>(out var text):
                builder.Append(text).Append(' ');
                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    if (item is JsonValue element && element.TryGetValue<string>(out var text))
                    {
                        builder.Append(text).Append(' ');
                    }
                }

                break;
            case IEnumerable items and not IDictionary and not ContentDocument:
                foreach (var item in items)
                {
                    if (item is string text)
                    {
                        builder.Append(text).Append(' ');
                    }
                }

                break;
        }
    }

    // The text nodes of a ProseMirror document, with a space after every other node, so that words in two paragraphs stay
    // apart and a word split by a mark stays whole.
    private static void AppendRichText(StringBuilder builder, string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        AppendNode(builder, root);
    }

    private static void AppendNode(StringBuilder builder, JsonNode? node)
    {
        if (node is not JsonObject element)
        {
            return;
        }

        if (element["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name == "text")
        {
            if (element["text"] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                builder.Append(text);
            }

            return;
        }

        if (element["content"] is JsonArray children)
        {
            foreach (var child in children)
            {
                AppendNode(builder, child);
            }
        }

        builder.Append(' ');
    }
}
