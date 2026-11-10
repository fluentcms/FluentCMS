using System.Globalization;
using System.Text.Json.Nodes;

namespace FluentCMS.Plugins.Mcp;

// What keeps a list within what an agent can take in (spec 5.4): a long text is shortened with a marker, and a list over
// the size limit ends at an entry, never inside one.
internal static class McpListResults
{
    public const string Truncated = "truncated";

    private const string Items = "items";

    // Room for the cursor and the mark of a list that is cut, which the list measured did not hold yet.
    private const int CutAllowance = 1024;

    // Shortens every text of the entries that is longer than the limit, at any depth, and says how long it was and which
    // tool reads it whole.
    public static void Shorten(JsonObject page, int maxLength, string whole)
    {
        if (page[Items] is JsonArray items)
        {
            Shorten(items, maxLength, whole);
        }
    }

    // How many entries from the first the list keeps to stay within the size: all of them when it fits, and one at
    // least, since an entry is never cut and an answer without one would go no further.
    public static int Fit(JsonObject page, int maxBytes)
    {
        if (page[Items] is not JsonArray items || items.Count == 0)
        {
            return 0;
        }

        var whole = McpArguments.Size(page);
        if (whole <= maxBytes)
        {
            return items.Count;
        }

        List<int> sizes = [.. items.Select(item => item is null ? 4 : McpArguments.Size(item))];
        // What is not an entry stays: the paging and the commas between the entries.
        var budget = maxBytes - (whole - sizes.Sum()) - CutAllowance;
        var kept = 0;
        long used = 0;
        while (kept < items.Count - 1 && used + sizes[kept] <= budget)
        {
            used += sizes[kept++];
        }

        return Math.Max(1, kept);
    }

    private static void Shorten(JsonNode? node, int maxLength, string whole)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var (name, value) in properties.ToList())
                {
                    if (Shortened(value, maxLength, whole) is { } text)
                    {
                        properties[name] = text;
                    }
                    else
                    {
                        Shorten(value, maxLength, whole);
                    }
                }

                break;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    if (Shortened(items[index], maxLength, whole) is { } text)
                    {
                        items[index] = text;
                    }
                    else
                    {
                        Shorten(items[index], maxLength, whole);
                    }
                }

                break;
        }
    }

    private static string? Shortened(JsonNode? node, int maxLength, string whole)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length <= maxLength)
        {
            return null;
        }

        // Not between the two halves of a character outside the basic plane.
        var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
        return string.Create(CultureInfo.InvariantCulture, $"{text.AsSpan(0, length)}… [shortened: {text.Length} characters in all; {whole} reads it whole]");
    }
}
