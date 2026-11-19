using System.Text.Json.Nodes;

namespace FluentCMS.Plugins.Media.Admin;

// The media types a picker takes, each a type such as image/png or a family such as image/*, as GET /media's type takes them.
internal static class MediaTypes
{
    public static bool Accepts(IReadOnlyList<string>? accept, string? mimeType)
    {
        if (accept is not { Count: > 0 })
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return false;
        }

        var type = mimeType.Split(';', 2)[0].Trim();
        return accept.Any(item => item.EndsWith("/*", StringComparison.Ordinal)
            ? type.Length > item.Length - 1 && type.StartsWith(item[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(item, type, StringComparison.OrdinalIgnoreCase));
    }

    // The types of a media field's "accept" setting: a type, a family or a list of them; every type without it.
    public static IReadOnlyList<string> OfField(JsonObject settings) => settings["accept"] switch
    {
        JsonValue value when value.TryGetValue(out string? one) && !string.IsNullOrWhiteSpace(one) => [one],
        JsonArray list => [.. list.Select(item => item is JsonValue text && text.TryGetValue(out string? type) ? type : null).OfType<string>().Where(type => type.Length > 0)],
        _ => [],
    };
}
