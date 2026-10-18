using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Core.Content;

public static class ResolvedFields
{
    // Puts each value at its JSON Pointer (RFC 6901) in a copy of the fields; a null value writes JSON null. The pointers come from
    // the reference index, so one that leads nowhere means the index and the stored version disagree.
    public static JsonElement Replace(JsonElement fields, IReadOnlyDictionary<string, JsonElement?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var root = JsonNode.Parse(fields.GetRawText())
            ?? throw new ArgumentException("The fields are a JSON object.", nameof(fields));

        foreach (var (pointer, value) in values)
        {
            Set(root, pointer, value is { } element ? JsonNode.Parse(element.GetRawText()) : null);
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static void Set(JsonNode root, string pointer, JsonNode? value)
    {
        var segments = pointer.Split('/');
        if (segments.Length < 2 || segments[0].Length != 0)
        {
            throw new ArgumentException($"'{pointer}' is not a pointer to a value inside the fields.", nameof(pointer));
        }

        var parent = root;
        for (var index = 1; index < segments.Length - 1; index++)
        {
            parent = Child(parent, Unescape(segments[index]), pointer)
                ?? throw new InvalidOperationException($"The fields hold null on the way to '{pointer}'.");
        }

        var last = Unescape(segments[^1]);
        switch (parent)
        {
            case JsonObject container when container.ContainsKey(last):
                container[last] = value;
                break;
            case JsonArray list when int.TryParse(last, out var position) && position >= 0 && position < list.Count:
                list[position] = value;
                break;
            default:
                throw new InvalidOperationException($"The fields have no value at '{pointer}'.");
        }
    }

    private static JsonNode? Child(JsonNode node, string segment, string pointer)
    {
        return node switch
        {
            JsonObject container when container.TryGetPropertyValue(segment, out var child) => child,
            JsonArray list when int.TryParse(segment, out var position) && position >= 0 && position < list.Count => list[position],
            _ => throw new InvalidOperationException($"The fields have no value at '{pointer}'."),
        };
    }

    private static string Unescape(string segment)
    {
        return segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
    }
}
