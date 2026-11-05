using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Api.Common;

// The flat response shape of spec 4.4: an entry's system and content fields side by side, relations inlined as the
// engine populated them, and a list as items with its paging.
internal static class ContentJson
{
    public static JsonObject Entry(ContentDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return (JsonObject)Node(entry)!;
    }

    public static JsonObject List(PagedResult<ContentDocument> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        JsonObject list = new()
        {
            ["items"] = new JsonArray([.. result.Items.Select(item => (JsonNode)Entry(item))]),
        };
        if (result.Total is { } total)
        {
            list["total"] = total;
        }

        if (result.Page is { } page)
        {
            list["page"] = page;
        }

        list["pageSize"] = result.PageSize;
        if (result.NextCursor is { } cursor)
        {
            list["nextCursor"] = cursor;
        }

        return list;
    }

    // EntryJson writes every value but a status, which the response names in words; a populated entry has one too.
    private static JsonNode? Node(object? value) => value switch
    {
        EntryStatus status => JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(status.ToString())),
        IEnumerable<KeyValuePair<string, object?>> pairs => Object(pairs),
        IEnumerable items and not string and not JsonNode => new JsonArray([.. items.Cast<object?>().Select(Node)]),
        _ => EntryJson.ToNode(value),
    };

    private static JsonObject Object(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        JsonObject result = [];
        foreach (var (name, value) in pairs)
        {
            result[name] = Node(value);
        }

        return result;
    }
}
