using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
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

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Text(JsonNode node) => node.ToJsonString(Options);

    public static string Date(DateOnly day) => day.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string Moment(DateTimeOffset moment) => moment.UtcDateTime.ToString(MomentFormat, CultureInfo.InvariantCulture);

    // The fields of an entry, which is what a version holds and what a change is decided on. System fields are not in it.
    public static JsonObject Fields(SchemaType type, ContentDocument entry)
    {
        JsonObject fields = [];
        foreach (var field in type.Fields)
        {
            entry.TryGetValue(field.Name, out var value);
            fields[field.Name] = ToNode(value);
        }

        return fields;
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

    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } => null,
        JsonElement element => JsonNode.Parse(element.GetRawText()),
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
        RichText document => JsonNode.Parse(document.Json),
        IEnumerable<KeyValuePair<string, object?>> pairs => ToObject(pairs),
        IEnumerable items => new JsonArray([.. items.Cast<object?>().Select(ToNode)]),
        _ => JsonSerializer.SerializeToNode(value, value.GetType(), Options),
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
