using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Lists;

namespace FluentCMS.Admin.Content;

// A field a list may filter on, with the filter its type offers.
internal sealed record FilterField(SchemaFieldDescription Field, ListFilterDefinition Filter)
{
    public string Name => Field.Name;

    // The values an option operand takes, with their labels: the options of a select, or the block types of a blocks field.
    // An option is a value, or an object with a value and a label.
    public IReadOnlyList<CmsOption> Options => Field.Settings[Field.Type == "blocks" ? "blocks" : "options"] is JsonArray options
        ? [.. options.Select(Option).OfType<CmsOption>()]
        : [];

    public string Label(string value) => Options.FirstOrDefault(option => option.Value == value)?.Text ?? value;

    private static CmsOption? Option(JsonNode? option) => option switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String => new CmsOption(value.GetValue<string>(), value.GetValue<string>()),
        JsonObject item when Text(item["value"]) is { } value => new CmsOption(value, Text(item["label"]) ?? value),
        _ => null,
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}

// The filters of a list in its address, as the API's grammar writes them (backend 4.4): filter[title][contains]=a, once per
// filter, so that the address of a list is the query it reads. A filter the list does not offer, or whose operand does not
// read as its field's, is dropped, as a malformed sort or page is.
internal static class ListFilters
{
    public const string Prefix = "filter[";

    // The longest search the API takes (FCMS2207).
    public const int MaxSearchLength = 256;

    private static readonly Dictionary<string, FilterOperator> Operators = Enum.GetValues<FilterOperator>().ToDictionary(Name, StringComparer.Ordinal);

    public static string Name(FilterOperator filterOperator) => filterOperator switch
    {
        FilterOperator.Equal => "eq",
        FilterOperator.NotEqual => "ne",
        FilterOperator.In => "in",
        FilterOperator.NotIn => "nin",
        FilterOperator.LessThan => "lt",
        FilterOperator.LessThanOrEqual => "lte",
        FilterOperator.GreaterThan => "gt",
        FilterOperator.GreaterThanOrEqual => "gte",
        FilterOperator.Between => "between",
        FilterOperator.Contains => "contains",
        FilterOperator.StartsWith => "startsWith",
        FilterOperator.IsNull => "null",
        FilterOperator.IsNotNull => "notNull",
        FilterOperator.HasBlock => "hasBlock",
        _ => throw new ArgumentOutOfRangeException(nameof(filterOperator), filterOperator, null),
    };

    public static bool TakesNoValue(FilterOperator filterOperator) => filterOperator is FilterOperator.IsNull or FilterOperator.IsNotNull;

    public static bool TakesList(FilterOperator filterOperator) => filterOperator is FilterOperator.In or FilterOperator.NotIn;

    // The fields of a type a filter may be set on, in schema order: those the caller reads, since the schema has no other,
    // whose type names its operators.
    public static IReadOnlyList<FilterField> Fields(SchemaTypeDescription type, ListFilterRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(registry);
        return [.. type.Fields.Select(field => registry.Find(field) is { } filter ? new FilterField(field, filter) : null).OfType<FilterField>()];
    }

    public static FilterField? Find(this IReadOnlyList<FilterField> fields, string name) => fields.FirstOrDefault(field => field.Name == name);

    // The filters of a query string, with or without its "?", in their order.
    public static IReadOnlyList<FieldFilter> Parse(string? query, IReadOnlyList<FilterField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        List<FieldFilter> filters = [];
        foreach (var (key, value) in Pairs(query))
        {
            if (Key(key) is not { } parsed
                || fields.Find(parsed.Path) is not { } field
                || !Operators.TryGetValue(parsed.Operator, out var filterOperator)
                || !field.Filter.Operators.Contains(filterOperator))
            {
                continue;
            }

            // A null test reads true, or false for its opposite, as the server does.
            if (TakesNoValue(filterOperator))
            {
                if (value is "false")
                {
                    filterOperator = filterOperator == FilterOperator.IsNull ? FilterOperator.IsNotNull : FilterOperator.IsNull;
                }
                else if (value is not ("" or "true"))
                {
                    continue;
                }
            }

            var (valid, operand) = OperandOf(field, filterOperator, value);
            var filter = new FieldFilter(parsed.Path, filterOperator, operand);
            if (valid && !filters.Contains(filter))
            {
                filters.Add(filter);
            }
        }

        return filters;
    }

    // The search of a query string, q=.
    public static string? SearchOf(string? query) => Pairs(query).LastOrDefault(pair => pair.Key == "q").Value;

    // The search of the address, trimmed; none when it is blank or too long for the API.
    public static string? ParseSearch(string? text) => text?.Trim() is { Length: > 0 and <= MaxSearchLength } search ? search : null;

    // The address of the list with these filters and this search, the rest of its query string kept but the page, since a
    // changed filter shows the first page.
    public static string Address(string relativeUri, IEnumerable<FieldFilter> filters, string? search)
    {
        ArgumentNullException.ThrowIfNull(relativeUri);
        ArgumentNullException.ThrowIfNull(filters);
        var mark = relativeUri.IndexOf('?', StringComparison.Ordinal);
        var path = mark < 0 ? relativeUri : relativeUri[..mark];
        var kept = (mark < 0 ? "" : relativeUri[(mark + 1)..])
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => Unescape(pair.Split('=', 2)[0]) is var key && !key.StartsWith(Prefix, StringComparison.Ordinal) && key is not ("q" or "page"));
        var written = filters.Select(filter => new ContentQuery { Filter = filter }.ToQueryString());
        var query = string.Join('&', kept.Concat(written).Concat(ParseSearch(search) is { } text ? [$"q={Uri.EscapeDataString(text)}"] : []));
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    // The values of the operand as text, one per input of a filter: none for a null test, two for between.
    public static IReadOnlyList<string> Texts(object? operand) => operand switch
    {
        null => [],
        IEnumerable items and not string => [.. items.Cast<object?>().Select(ContentQuery.Text)],
        _ => [ContentQuery.Text(operand)],
    };

    // The operand of a filter from the text of its inputs, typed as its field's; not valid when any does not read.
    public static (bool Valid, object? Operand) Operand(FilterField field, FilterOperator filterOperator, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(texts);
        if (TakesNoValue(filterOperator))
        {
            return (true, null);
        }

        var values = texts.Select(text => Value(field, text.Trim())).ToList();
        var count = filterOperator switch
        {
            FilterOperator.Between => values.Count == 2,
            FilterOperator.In or FilterOperator.NotIn => values.Count > 0,
            _ => values.Count == 1,
        };
        if (!count || values.Any(value => value is null))
        {
            return (false, null);
        }

        return (true, filterOperator is FilterOperator.Between || TakesList(filterOperator) ? values.Cast<object>().ToList() : values[0]);
    }

    private static (bool Valid, object? Operand) OperandOf(FilterField field, FilterOperator filterOperator, string text)
    {
        IReadOnlyList<string> texts = TakesNoValue(filterOperator) ? [] : filterOperator is FilterOperator.Between || TakesList(filterOperator) ? Items(text) : [text];
        return Operand(field, filterOperator, texts);
    }

    private static object? Value(FilterField field, string text) => field.Filter.Operand switch
    {
        _ when text.Length == 0 => null,
        FilterOperand.WholeNumber => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null,
        FilterOperand.DecimalNumber => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null,
        FilterOperand.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null,
        FilterOperand.DateTime => Moment(text),
        FilterOperand.Boolean => text switch { "true" => true, "false" => false, _ => null },
        FilterOperand.Option => field.Options.Any(option => option.Value == text) ? text : null,
        FilterOperand.Id => Guid.TryParse(text, out var id) ? id : null,
        _ => text,
    };

    private static (string Path, string Operator)? Key(string key)
    {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal) || !key.EndsWith(']'))
        {
            return null;
        }

        var parts = key[Prefix.Length..^1].Split("][", StringSplitOptions.None);
        return parts is [{ Length: > 0 } path, { Length: > 0 } name] ? (path, name) : null;
    }

    // A moment as the grammar writes it, with an offset or Z; the other forms of a culture are not read.
    public static DateTimeOffset? Moment(string? text) =>
        DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            ? moment.ToUniversalTime()
            : null;

    // The values of a list as one text, which Items reads back.
    public static string Join(IEnumerable<string> values) => string.Join(", ", values.Select(ContentQuery.EscapeItem));

    // A list operand is comma separated, with \, and \\ for a comma and a backslash in a value.
    public static List<string> Items(string text)
    {
        List<string> items = [];
        var item = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                item.Append(text[++index]);
            }
            else if (text[index] == ',')
            {
                items.Add(item.ToString());
                item.Clear();
            }
            else
            {
                item.Append(text[index]);
            }
        }

        items.Add(item.ToString());
        return items;
    }

    private static IEnumerable<(string Key, string Value)> Pairs(string? query) =>
        (query ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => (Unescape(parts[0]), parts.Length > 1 ? Unescape(parts[1]) : ""));

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}
