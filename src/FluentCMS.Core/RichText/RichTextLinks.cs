using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.ProseMirror;

// A URL in rich text that SafeUrl refuses, such as javascript:, under a key that holds a URL (SafeUrl.UrlAttributes) at any
// depth in the attributes of a node or mark, the data of an inline block of a known type aside, which has its block's
// rules. A value under such a key that is neither null nor a string is refused too, since a front end that binds it reads
// it as a string. A write refuses it. One stored before that rule stays in the row and is dropped when read: a link mark
// goes whole, keeping its text, and any other key goes on its own.
internal static class RichTextLinks
{
    private const string Data = "data";

    public static IEnumerable<string> UnsafePaths(DocumentNode document, SchemaModel schema, SafeUrl links)
    {
        List<string> found = [];
        Visit(document, "$", schema, links, found);
        return found;
    }

    // False when no key that holds a URL in the text can hold an unsafe value, so that a read of an ordinary document
    // parses nothing for this.
    public static bool MayHoldUnsafe(string json, SafeUrl links) => FirstUnsafeScheme(json, links) is not null;

    // What the first value that may be unsafe starts with, for a log: its scheme, never the whole URL.
    public static string? FirstUnsafeScheme(string json, SafeUrl links)
    {
        if (HasEscapedAscii(json))
        {
            return "(escaped)";
        }

        foreach (var name in SafeUrl.UrlAttributes)
        {
            var quoted = $"\"{name}\"";
            for (var at = json.IndexOf(quoted, StringComparison.OrdinalIgnoreCase); at >= 0; at = json.IndexOf(quoted, at + quoted.Length, StringComparison.OrdinalIgnoreCase))
            {
                if (UnsafeValueAt(json, at + quoted.Length, links) is { } scheme)
                {
                    return scheme;
                }
            }
        }

        return null;
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
            var skip = TypeOf(item) == RichTextNodeTypes.InlineBlock && IsKnownBlock(attributes, schema) ? Data : null;
            changed = DropIn(attributes, links, skip);
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

                if (TypeOf(mark) == RichTextMarkTypes.Link && markAttributes.ContainsKey("href") && Unsafe(markAttributes["href"], links))
                {
                    marks.Remove(mark);
                    removed = true;
                    changed = true;
                }
                else
                {
                    changed |= DropIn(markAttributes, links, skip: null);
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
        var known = node is InlineBlockNode block && schema.FindBlock(block.BlockType) is not null;
        foreach (var (name, value) in node.Attributes)
        {
            if (!(known && name == Data))
            {
                VisitValue(name, value, $"{path}.attrs.{name}", links, found);
            }
        }

        for (var index = 0; index < node.Marks.Count; index++)
        {
            foreach (var (name, value) in node.Marks[index].Attributes)
            {
                VisitValue(name, value, $"{path}.marks[{index}].attrs.{name}", links, found);
            }
        }

        for (var index = 0; index < node.Content.Count; index++)
        {
            Visit(node.Content[index], $"{path}.content[{index}]", schema, links, found);
        }
    }

    private static void VisitValue(string name, JsonElement value, string path, SafeUrl links, List<string> found)
    {
        if (SafeUrl.IsUrlAttribute(name) && Unsafe(value, links))
        {
            found.Add(path);
        }
        else
        {
            VisitInside(value, path, links, found);
        }
    }

    private static void VisitInside(JsonElement value, string path, SafeUrl links, List<string> found)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    VisitValue(property.Name, property.Value, $"{path}.{property.Name}", links, found);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    VisitInside(item, $"{path}[{index++}]", links, found);
                }

                break;
        }
    }

    // Drops what is unsafe from an object and everything inside it, but for the key skip.
    private static bool DropIn(JsonNode node, SafeUrl links, string? skip)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject item:
                foreach (var (name, value) in item.ToList())
                {
                    if (name == skip)
                    {
                        continue;
                    }

                    if (SafeUrl.IsUrlAttribute(name) && Unsafe(value, links))
                    {
                        item.Remove(name);
                        changed = true;
                    }
                    else if (value is not null)
                    {
                        changed |= DropIn(value, links, skip: null);
                    }
                }

                break;
            case JsonArray items:
                foreach (var value in items)
                {
                    changed |= value is not null && DropIn(value, links, skip: null);
                }

                break;
        }

        return changed;
    }

    private static bool Unsafe(JsonElement value, SafeUrl links) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => false,
            JsonValueKind.String => !links.IsSafe(value.GetString()!),
            _ => true,
        };

    private static bool Unsafe(JsonNode? value, SafeUrl links) =>
        value switch
        {
            null => false,
            JsonValue text when text.GetValueKind() == JsonValueKind.String => !links.IsSafe(text.GetValue<string>()),
            _ => true,
        };

    private static string? TypeOf(JsonObject item) =>
        item["type"] is JsonValue type && type.TryGetValue<string>(out var name) ? name : null;

    private static bool IsKnownBlock(JsonObject attributes, SchemaModel schema) =>
        attributes["blockType"] is JsonValue type && type.TryGetValue<string>(out var name) && schema.FindBlock(name) is not null;

    // The value after a key: null when it is null or a string the rule allows, else the start of its scheme. A key with no
    // colon after it is text, not a key.
    private static string? UnsafeValueAt(string json, int index, SafeUrl links)
    {
        index = SkipWhitespace(json, index);
        if (index >= json.Length || json[index] != ':')
        {
            return null;
        }

        index = SkipWhitespace(json, index + 1);
        if (string.CompareOrdinal(json, index, "null", 0, 4) == 0)
        {
            return null;
        }

        if (index >= json.Length || json[index] != '"')
        {
            return "(not a string)";
        }

        var end = index + 1;
        var escaped = false;
        while (end < json.Length && json[end] != '"')
        {
            escaped |= json[end] == '\\';
            end += json[end] == '\\' ? 2 : 1;
        }

        var value = json[(index + 1)..Math.Min(end, json.Length)];
        if (!escaped && links.IsSafe(value))
        {
            return null;
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? "(no scheme)" : value[..Math.Min(colon, 32)].Trim();
    }

    private static int SkipWhitespace(string json, int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }

        return index;
    }

    // An escaped letter or colon can spell a key that holds a URL, which a plain search does not find.
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
