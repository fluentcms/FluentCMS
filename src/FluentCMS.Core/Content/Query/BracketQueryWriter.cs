using System.Collections;
using System.Globalization;
using System.Text;
using FluentCMS.Content;

namespace FluentCMS.Core.Content.Query;

// A ContentQuery as the REST grammar, in the order of spec 4.4, so that a query can be logged, cached by URL or sent
// on. BracketQueryParser reads back the same query.
internal static class BracketQueryWriter
{
    public static string Write(ContentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<(string Key, string Value)> pairs = [];
        if (query.Filter is { } filter)
        {
            WriteFilter(filter, "filter", pairs);
        }

        if (query.Sort.Count > 0)
        {
            pairs.Add(("sort", string.Join(',', query.Sort.Select(field => (field.Descending ? "-" : "") + field.Path))));
        }

        WritePage(query, pairs);

        if (query.Cursor is { } cursor)
        {
            pairs.Add(("cursor", cursor));
        }

        if (query.Select.Count > 0)
        {
            pairs.Add(("fields", string.Join(',', query.Select)));
        }

        if (query.Populate.Count > 0)
        {
            pairs.Add(("populate", string.Join(',', query.Populate)));
        }

        Add("depth", query.PopulateDepth?.ToString(CultureInfo.InvariantCulture));
        Add("status", query.Status switch { QueryStatus.Published => "published", QueryStatus.Draft => "draft", _ => null });
        Add("locale", query.Locale);
        Add("fallback", query.Fallback == LocaleFallback.None ? "none" : null);
        Add("site", query.Site?.ToString());
        Add("richText", query.RichTextFormat);

        if (query.Page is { WithTotal: false })
        {
            pairs.Add(("withTotal", "false"));
        }

        return string.Join('&', pairs.Select(pair => $"{EscapeKey(pair.Key)}={EscapeValue(pair.Value)}"));

        void Add(string key, string? value)
        {
            if (value is not null)
            {
                pairs.Add((key, value));
            }
        }
    }

    private static void WritePage(ContentQuery query, List<(string Key, string Value)> pairs)
    {
        if (query.Page is not { } page)
        {
            return;
        }

        if (page.Number is { } number)
        {
            pairs.Add(("page", number.ToString(CultureInfo.InvariantCulture)));
        }

        if (page.Offset is { } offset)
        {
            pairs.Add(("offset", offset.ToString(CultureInfo.InvariantCulture)));
        }

        if (page.Size is { } size)
        {
            pairs.Add((page.Offset is null ? "pageSize" : "limit", size.ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void WriteFilter(ContentFilter filter, string prefix, List<(string Key, string Value)> pairs)
    {
        switch (filter)
        {
            case FieldFilter field:
                pairs.Add(($"{prefix}[{field.Path}][{BracketQueryParser.OperatorName(field.Operator)}]", Operand(field)));
                break;
            case Not negated:
                WriteFilter(negated.Filter, $"{prefix}[not]", pairs);
                break;
            case Or any:
                for (var index = 0; index < any.Filters.Count; index++)
                {
                    WriteFilter(any.Filters[index], $"{prefix}[or][{index}]", pairs);
                }

                break;
            case And every when NeedsIndexes(every):
                for (var index = 0; index < every.Filters.Count; index++)
                {
                    WriteFilter(every.Filters[index], $"{prefix}[and][{index}]", pairs);
                }

                break;
            case And every:
                foreach (var item in every.Filters)
                {
                    WriteFilter(item, prefix, pairs);
                }

                break;
        }
    }

    // Conditions side by side are an implicit And, unless their keys would merge when read back.
    private static bool NeedsIndexes(And every) =>
        every.Filters.Any(item => item is And)
        || every.Filters.Count(item => item is Or) > 1
        || every.Filters.Count(item => item is Not) > 1
        || every.Filters.OfType<FieldFilter>().GroupBy(field => (field.Path, field.Operator)).Any(group => group.Count() > 1);

    private static string Operand(FieldFilter field) => field.Operator switch
    {
        FilterOperator.IsNull or FilterOperator.IsNotNull => "true",
        _ when field.Value is IEnumerable items and not string => string.Join(',', items.Cast<object?>().Select(item => EscapeItem(Text(item)))),
        FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between => EscapeItem(Text(field.Value)),
        _ => Text(field.Value),
    };

    private static string Text(object? value) => value switch
    {
        null => "",
        bool flag => flag ? "true" : "false",
        DateTimeOffset moment => moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string EscapeItem(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal);

    // Brackets and the commas of the grammar stay readable; everything else in a key or value is percent-encoded.
    private static string EscapeKey(string key) =>
        Uri.EscapeDataString(key).Replace("%5B", "[", StringComparison.Ordinal).Replace("%5D", "]", StringComparison.Ordinal);

    private static string EscapeValue(string value) =>
        new StringBuilder(Uri.EscapeDataString(value)).Replace("%2C", ",").Replace("%2A", "*").ToString();
}
