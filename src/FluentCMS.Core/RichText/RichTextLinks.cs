using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

// A URL in rich text that SafeUrl refuses, such as javascript:, in an attribute that holds a URL (SafeUrl.UrlAttributes) of
// any node or mark, or under such a key anywhere in the data of an inline block of a type the schema does not know. A write
// refuses it. One stored before that rule stays in the row and is dropped when read: a link mark goes whole, keeping its
// text, and any other attribute goes on its own.
internal static class RichTextLinks
{
    private const string Data = "data";

    public static IEnumerable<string> UnsafePaths(DocumentNode document, SchemaModel schema, SafeUrl links)
    {
        List<string> found = [];
        Visit(document, "$", schema, links, found);
        return found;
    }

    // False when no URL attribute in the text can be unsafe, so that a read of an ordinary document parses nothing.
    public static bool MayHoldUnsafe(string json, SafeUrl links)
    {
        if (HasEscapedAscii(json))
        {
            return true;
        }

        foreach (var name in SafeUrl.UrlAttributes)
        {
            var quoted = $"\"{name}\"";
            for (var at = json.IndexOf(quoted, StringComparison.OrdinalIgnoreCase); at >= 0; at = json.IndexOf(quoted, at + quoted.Length, StringComparison.OrdinalIgnoreCase))
            {
                if (StringValueAt(json, at + quoted.Length) is { } value && (value.Contains('\\', StringComparison.Ordinal) || !links.IsSafe(value)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool DropUnsafe(JsonNode? node, SchemaModel schema, SafeUrl links)
    {
        if (node is not JsonObject item)
        {
            return false;
        }

        var changed = false;
        if (item["attrs"] is JsonObject attributes)
        {
            changed = DropAttributes(attributes, links);
            if (TypeOf(item) == RichTextNodeTypes.InlineBlock && !IsKnownBlock(attributes, schema) && attributes[Data] is { } data)
            {
                changed |= DropInData(data, links);
            }
        }

        if (item["marks"] is JsonArray marks)
        {
            var removed = false;
            foreach (var mark in marks.OfType<JsonObject>().ToList())
            {
                if (mark["attrs"] is not JsonObject markAttributes)
                {
                    continue;
                }

                if (TypeOf(mark) == RichTextMarkTypes.Link && Unsafe(markAttributes["href"], links))
                {
                    marks.Remove(mark);
                    removed = true;
                    changed = true;
                }
                else
                {
                    changed |= DropAttributes(markAttributes, links);
                }
            }

            if (removed && marks.Count == 0)
            {
                item.Remove("marks");
            }
        }

        if (item["content"] is JsonArray content)
        {
            foreach (var child in content)
            {
                changed |= DropUnsafe(child, schema, links);
            }
        }

        return changed;
    }

    private static void Visit(RichTextNode node, string path, SchemaModel schema, SafeUrl links, List<string> found)
    {
        foreach (var (name, value) in node.Attributes)
        {
            if (Unsafe(name, value, links))
            {
                found.Add($"{path}.attrs.{name}");
            }
        }

        if (node is InlineBlockNode block && schema.FindBlock(block.BlockType) is null && node.Attributes.TryGetValue(Data, out var data))
        {
            VisitData(data, $"{path}.attrs.{Data}", links, found);
        }

        for (var index = 0; index < node.Marks.Count; index++)
        {
            foreach (var (name, value) in node.Marks[index].Attributes)
            {
                if (Unsafe(name, value, links))
                {
                    found.Add($"{path}.marks[{index}].attrs.{name}");
                }
            }
        }

        for (var index = 0; index < node.Content.Count; index++)
        {
            Visit(node.Content[index], $"{path}.content[{index}]", schema, links, found);
        }
    }

    private static void VisitData(JsonElement value, string path, SafeUrl links, List<string> found)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (Unsafe(property.Name, property.Value, links))
                    {
                        found.Add($"{path}.{property.Name}");
                    }
                    else
                    {
                        VisitData(property.Value, $"{path}.{property.Name}", links, found);
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    VisitData(item, $"{path}[{index++}]", links, found);
                }

                break;
        }
    }

    private static bool DropAttributes(JsonObject attributes, SafeUrl links)
    {
        var unsafeNames = attributes.Where(pair => SafeUrl.IsUrlAttribute(pair.Key) && Unsafe(pair.Value, links)).Select(pair => pair.Key).ToList();
        foreach (var name in unsafeNames)
        {
            attributes.Remove(name);
        }

        return unsafeNames.Count > 0;
    }

    private static bool DropInData(JsonNode data, SafeUrl links)
    {
        var changed = false;
        switch (data)
        {
            case JsonObject item:
                changed = DropAttributes(item, links);
                foreach (var (_, value) in item)
                {
                    changed |= value is not null && DropInData(value, links);
                }

                break;
            case JsonArray items:
                foreach (var value in items)
                {
                    changed |= value is not null && DropInData(value, links);
                }

                break;
        }

        return changed;
    }

    private static bool Unsafe(string name, JsonElement value, SafeUrl links) =>
        SafeUrl.IsUrlAttribute(name) && value.ValueKind == JsonValueKind.String && !links.IsSafe(value.GetString()!);

    private static bool Unsafe(JsonNode? value, SafeUrl links) =>
        value is JsonValue text && text.TryGetValue<string>(out var url) && !links.IsSafe(url);

    private static string? TypeOf(JsonObject item) =>
        item["type"] is JsonValue type && type.TryGetValue<string>(out var name) ? name : null;

    private static bool IsKnownBlock(JsonObject attributes, SchemaModel schema) =>
        attributes["blockType"] is JsonValue type && type.TryGetValue<string>(out var name) && schema.FindBlock(name) is not null;

    // The string after a key, past the colon and any whitespace; null when the value is not a string.
    private static string? StringValueAt(string json, int index)
    {
        index = SkipWhitespace(json, index);
        if (index >= json.Length || json[index] != ':')
        {
            return null;
        }

        index = SkipWhitespace(json, index + 1);
        if (index >= json.Length || json[index] != '"')
        {
            return null;
        }

        var end = index + 1;
        while (end < json.Length && json[end] != '"')
        {
            end += json[end] == '\\' ? 2 : 1;
        }

        return json[(index + 1)..Math.Min(end, json.Length)];
    }

    private static int SkipWhitespace(string json, int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }

        return index;
    }

    // An escaped letter or colon, as in "\u0068ref", can spell a URL attribute that a plain search does not find.
    private static bool HasEscapedAscii(string json)
    {
        for (var at = json.IndexOf("\\u00", StringComparison.Ordinal); at >= 0 && at + 5 < json.Length; at = json.IndexOf("\\u00", at + 4, StringComparison.Ordinal))
        {
            if (json[at + 4] is >= '4' and <= '7' || json.AsSpan(at + 4, 2).Equals("3A", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
