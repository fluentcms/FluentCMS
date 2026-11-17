using System.Collections;
using System.Globalization;
using System.Text;
using FluentCMS.Admin.Lists;

namespace FluentCMS.Admin.Content;

internal enum ContentStatus
{
    Published,
    Draft,
}

// A read of a content list. FluentCMS.Client's ContentQuery is not reused, since that assembly does not trim (AQ5); this
// one writes the same query string, which clients/filter-cases.json holds for both.
internal sealed record ContentQuery
{
    public ContentFilter? Filter { get; init; }

    // The text of a search, q=, which the search index answers (backend 4.4).
    public string? Search { get; init; }

    public IReadOnlyList<SortField> Sort { get; init; } = [];

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public string? Cursor { get; init; }

    public IReadOnlyList<string> Fields { get; init; } = [];

    public IReadOnlyList<string> Populate { get; init; } = [];

    public int? Depth { get; init; }

    public ContentStatus? Status { get; init; }

    public string? Locale { get; init; }

    // Asks for the number of entries that match, which a cursor read never has.
    public bool? WithTotal { get; init; }

    // The query string of backend spec 4.4, in its order and with its escaping, without the leading "?". A list operand is
    // comma separated, with \, and \\ for a comma and a backslash in a value.
    public string ToQueryString()
    {
        List<(string Key, string Value)> pairs = [];
        if (Filter is { } filter)
        {
            WriteFilter(filter, "filter", pairs);
        }

        Add("q", Search);
        if (Sort.Count > 0)
        {
            pairs.Add(("sort", string.Join(',', Sort.Select(field => (field.Descending ? "-" : "") + field.Path))));
        }

        Add("page", Page?.ToString(CultureInfo.InvariantCulture));
        Add("pageSize", PageSize?.ToString(CultureInfo.InvariantCulture));
        Add("cursor", Cursor);
        Add("fields", Fields.Count > 0 ? string.Join(',', Fields) : null);
        Add("populate", Populate.Count > 0 ? string.Join(',', Populate) : null);
        Add("depth", Depth?.ToString(CultureInfo.InvariantCulture));
        Add("status", Status switch { ContentStatus.Published => "published", ContentStatus.Draft => "draft", _ => null });
        Add("locale", Locale);
        Add("withTotal", WithTotal switch { true => "true", false => "false", null => null });

        return string.Join('&', pairs.Select(pair => $"{EscapeKey(pair.Key)}={EscapeValue(pair.Value)}"));

        void Add(string key, string? value)
        {
            if (value is not null)
            {
                pairs.Add((key, value));
            }
        }
    }

    private static void WriteFilter(ContentFilter filter, string prefix, List<(string Key, string Value)> pairs)
    {
        switch (filter)
        {
            case And { Filters.Count: 0 } or Or { Filters.Count: 0 }:
                throw new ArgumentException("An And or Or node needs at least one condition.", nameof(filter));
            case FieldFilter { Operator: FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between, Value: null or ICollection { Count: 0 } } field:
                throw new ArgumentException($"The filter on '{field.Path}' needs at least one value.", nameof(filter));
            case FieldFilter field:
                pairs.Add(($"{prefix}[{field.Path}][{ListFilters.Name(field.Operator)}]", Operand(field)));
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

    // A moment in UTC with Z and no trailing zeros in its fraction, which a JavaScript Date also gives.
    internal static string Text(object? value) => value switch
    {
        null => "",
        bool flag => flag ? "true" : "false",
        DateTimeOffset moment => moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        DateTime moment => moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Guid id => id.ToString("D"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    internal static string EscapeItem(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal);

    // Brackets and the commas of the grammar stay readable; everything else in a key or value is percent-encoded.
    private static string EscapeKey(string key) =>
        Uri.EscapeDataString(key).Replace("%5B", "[", StringComparison.Ordinal).Replace("%5D", "]", StringComparison.Ordinal);

    private static string EscapeValue(string value) =>
        new StringBuilder(Uri.EscapeDataString(value)).Replace("%2C", ",").Replace("%2A", "*").ToString();
}
