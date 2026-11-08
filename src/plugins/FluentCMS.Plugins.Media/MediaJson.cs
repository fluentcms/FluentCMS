using System.Text.Encodings.Web;
using System.Text.Json;

namespace FluentCMS.Plugins.Media;

// The JSON columns of an asset: alt and caption as maps of locale to text, and the tags as an array. Tags are written
// without escaping what JSON allows as it is, so that a list filters by a tag with a search for its JSON string in the
// column on every database; a tag holds no quote, backslash or control character, which would be escaped.
internal static class MediaJson
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Dictionary<string, string> Map(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) is { } map
            ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Sets or, for null, removes the text of a locale.
    public static string Set(string json, string locale, string? text)
    {
        var map = Map(json);
        if (text is null)
        {
            map.Remove(locale);
        }
        else
        {
            map[locale] = text;
        }

        return JsonSerializer.Serialize(new SortedDictionary<string, string>(map, StringComparer.Ordinal), Options);
    }

    public static List<string> Tags(string json) => JsonSerializer.Deserialize<List<string>>(json, Options) ?? [];

    public static string OfTags(IReadOnlyList<string> tags) => JsonSerializer.Serialize(tags, Options);

    public static string TagNeedle(string tag) => JsonSerializer.Serialize(tag, Options);
}
