using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// The JSON an entry is stored, snapshotted and compared in. A date inside JSON has exactly the form the block filters
// compare with (#2692), so that a filter on a block finds what was written.
internal static class EntryJson
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string MomentFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public static string Text(JsonNode node) => node.ToJsonString(JsonDepth.Serializer);

    public static string Date(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string Moment(DateTimeOffset moment) => moment.UtcDateTime.ToString(MomentFormat, CultureInfo.InvariantCulture);

    // Counts the calls of Fields in the flow that set it, so that a test can see how often a write copies its entry.
    public static AsyncLocal<StrongBox<int>?> FieldsCalls { get; } = new();

    // The fields of an entry, which is what a version holds and what a change is decided on. System fields are not in it.
    public static JsonObject Fields(SchemaType type, ContentDocument entry)
    {
        if (FieldsCalls.Value is { } calls)
        {
            calls.Value++;
        }

        JsonObject fields = [];
        foreach (var field in type.Fields)
        {
            entry.TryGetValue(field.Name, out var value);
            fields[field.Name] = ToNode(value);
        }

        return fields;
    }

    // The fields of an entry as Fields reads them, without copying them.
    public static void ThrowIfUnreadable(SchemaType type, ContentDocument entry)
    {
        foreach (var field in type.Fields)
        {
            if (entry.TryGetValue(field.Name, out var value))
            {
                ThrowIfUnreadableValue(value);
            }
        }
    }

    private static void ThrowIfUnreadableValue(object? value)
    {
        switch (value)
        {
            case JsonNode node:
                ThrowIfUnreadable(node);
                break;
            case JsonElement { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } element:
                ThrowIfUnreadable(JsonDepth.Parse(element.GetRawText()));
                break;
            case RichText document:
                ThrowIfUnreadable(JsonDepth.Parse(document.Json));
                break;
            case string:
                break;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                foreach (var (_, inner) in pairs)
                {
                    ThrowIfUnreadableValue(inner);
                }

                break;
            case IEnumerable items:
                foreach (var item in items)
                {
                    ThrowIfUnreadableValue(item);
                }

                break;
        }
    }

    // A JsonObject reads its keys when they are first asked for, and only then finds one that is repeated.
    public static void ThrowIfUnreadable(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var (_, value) in properties)
                {
                    ThrowIfUnreadable(value);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    ThrowIfUnreadable(item);
                }

                break;
        }
    }

    public static bool Same(object? left, object? right) => JsonNode.DeepEquals(ToNode(left), ToNode(right));

    // A provider may give back other text for the same document, such as jsonb with its keys reordered and spaced, so two
    // documents are the same when they hold the same JSON. One that cannot be read is only the same as its own text.
    public static bool SameDocument(RichText left, RichText right)
    {
        if (string.Equals(left.Json, right.Json, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return JsonNode.DeepEquals(JsonDepth.Parse(left.Json), JsonDepth.Parse(right.Json));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Only a short text can hold the empty document, so a longer one is not parsed to learn that it does not.
    public static bool IsEmptyDocument(RichText document) =>
        document.Json.Length <= 2 * RichText.Empty.Json.Length && SameDocument(document, RichText.Empty);

    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => null,
        JsonElement element => JsonDepth.Parse(element.GetRawText()),
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        long number => JsonValue.Create(number),
        int number => JsonValue.Create(number),
        decimal number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        DateOnly day => JsonValue.Create(Date(day)),
        DateTimeOffset moment => JsonValue.Create(Moment(moment)),
        Guid id => JsonValue.Create(id.ToString("D", CultureInfo.InvariantCulture)),
        MediaRef media => JsonValue.Create(media.Id.ToString("D", CultureInfo.InvariantCulture)),
        RichText document => JsonDepth.Parse(document.Json),
        IEnumerable<KeyValuePair<string, object?>> pairs => ToObject(pairs),
        IEnumerable items => new JsonArray([.. items.Cast<object?>().Select(ToNode)]),
        _ => JsonSerializer.SerializeToNode(value, value.GetType(), JsonDepth.Serializer),
    };

    private static JsonObject ToObject(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        JsonObject result = [];
        foreach (var (name, value) in pairs)
        {
            result[name] = ToNode(value);
        }

        return result;
    }
}

// Where a stored field sits in an entry: the path of a flattened column goes through the groups above it.
internal static class EntryPaths
{
    public static object? ValueAt(ContentDocument entry, StoredField stored, SchemaType type)
    {
        object? current = entry;
        foreach (var name in Query.QueryPaths.ApiPath(type, stored).Split('.'))
        {
            if (current is not ContentDocument group || !group.TryGetValue(name, out current))
            {
                return null;
            }
        }

        return current;
    }

    // Sets a stored field, making the groups above it where a single component was absent.
    public static void SetAt(ContentDocument entry, StoredField stored, SchemaType type, object? value)
    {
        var names = Query.QueryPaths.ApiPath(type, stored).Split('.');
        var group = entry;
        foreach (var name in names[..^1])
        {
            if (group.GetValueOrDefault(name) is not ContentDocument inner)
            {
                inner = [];
                group[name] = inner;
            }

            group = inner;
        }

        group[names[^1]] = value;
    }

    public static string ReferenceTarget(SchemaField field) => field.Relation?.Target ?? field.FieldType;
}
