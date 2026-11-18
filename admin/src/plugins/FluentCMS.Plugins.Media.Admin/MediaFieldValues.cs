using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Plugins.Media.Admin.Api;

namespace FluentCMS.Plugins.Media.Admin;

// The value of a media field: a read gives an asset as { "id", "type": "media" }, or the asset when populated; a write sends
// an id, or a list of ids in order, as the media plugin takes them.
internal static class MediaFieldValues
{
    public static IReadOnlyList<Guid> Ids(JsonNode? value) => value switch
    {
        JsonArray items => [.. items.Select(Id).OfType<Guid>()],
        _ => Id(value) is { } id ? [id] : [],
    };

    public static JsonNode? Write(IReadOnlyList<Guid> ids, bool multiple) => multiple
        ? new JsonArray([.. ids.Select(id => (JsonNode)JsonValue.Create(id.ToString("D")))])
        : ids.Count > 0 ? JsonValue.Create(ids[^1].ToString("D")) : null;

    // The assets of a populated value; one the read did not populate, as one the caller cannot read, has its id only.
    public static IReadOnlyList<MediaFieldAsset> Assets(JsonNode? value) => value switch
    {
        JsonArray items => [.. items.Select(Asset).OfType<MediaFieldAsset>()],
        _ => Asset(value) is { } asset ? [asset] : [],
    };

    private static Guid? Id(JsonNode? node) => node switch
    {
        JsonValue text when text.TryGetValue(out string? id) && Guid.TryParse(id, out var guid) && guid != Guid.Empty => guid,
        JsonObject item => Id(item["id"]),
        _ => null,
    };

    private static MediaFieldAsset? Asset(JsonNode? node)
    {
        if (node is not JsonObject item || item.ContainsKey("fileName") is false)
        {
            return Id(node) is { } id ? new MediaFieldAsset { Id = id } : null;
        }

        try
        {
            return item.Deserialize(MediaJsonContext.Default.MediaFieldAsset);
        }
        catch (JsonException)
        {
            return Id(node) is { } id ? new MediaFieldAsset { Id = id } : null;
        }
    }
}
