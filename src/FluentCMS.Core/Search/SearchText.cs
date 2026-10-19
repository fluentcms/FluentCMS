using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FluentCMS.Core.Search;

// What an entry is found by: every string and number in its fields, a fact's value and source included, in field order, as the
// lowercase words SearchTerms splits a query into. Indexing words rather than raw text keeps both engines' tokenizers out of it, so
// "19.99" or "A-4411" match the same way on PostgreSQL and SQLite. The migrations that index existing entries do the same.
public static class SearchText
{
    public static string From(JsonElement fields)
    {
        var text = new StringBuilder();
        Append(fields, text);
        return string.Join(' ', SearchTerms.Words(text.ToString()));
    }

    private static void Append(JsonElement value, StringBuilder text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    Append(property.Value, text);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    Append(item, text);
                }

                break;
            case JsonValueKind.String:
                text.Append(value.GetString()).Append(' ');
                break;
            case JsonValueKind.Number:
                text.Append(value.GetRawText()).Append(' ');
                break;
        }
    }
}

// A query split into the words both indexes match: runs of letters and digits, lowercased, as the indexes' tokenizers split text.
public static partial class SearchTerms
{
    public const int MaxTerms = 10;
    public const int MaxTermLength = 64;

    public static IReadOnlyList<string> From(string? query)
    {
        return query is null ? [] : [.. Words(query).Distinct(StringComparer.Ordinal)];
    }

    internal static IEnumerable<string> Words(string text)
    {
        return Word().Matches(text).Select(match => match.Value.ToLower(CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
