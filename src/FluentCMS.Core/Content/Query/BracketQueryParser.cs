using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Content.Query;

// The REST grammar of spec 4.4 as a ContentQuery. It knows nothing of the schema: a path that cannot exist in any
// schema fails here, and one that does not exist in this schema fails in the compiler. A list operand is comma
// separated, with \, and \\ for a comma and a backslash in a value.
internal static class BracketQueryParser
{
    private const string Filter = "filter";
    private const string OrToken = "or";
    private const string AndToken = "and";
    private const string NotToken = "not";
    private const int MaxFilterDepth = 16;

    private static readonly FrozenDictionary<string, FilterOperator> Operators = new Dictionary<string, FilterOperator>(StringComparer.Ordinal)
    {
        ["eq"] = FilterOperator.Equal,
        ["ne"] = FilterOperator.NotEqual,
        ["in"] = FilterOperator.In,
        ["nin"] = FilterOperator.NotIn,
        ["lt"] = FilterOperator.LessThan,
        ["lte"] = FilterOperator.LessThanOrEqual,
        ["gt"] = FilterOperator.GreaterThan,
        ["gte"] = FilterOperator.GreaterThanOrEqual,
        ["between"] = FilterOperator.Between,
        ["contains"] = FilterOperator.Contains,
        ["startsWith"] = FilterOperator.StartsWith,
        ["null"] = FilterOperator.IsNull,
        ["notNull"] = FilterOperator.IsNotNull,
        ["hasBlock"] = FilterOperator.HasBlock,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string OperatorName(FilterOperator filterOperator) => Operators.First(item => item.Value == filterOperator).Key;

    public static ContentQuery Parse(string queryString)
    {
        ArgumentNullException.ThrowIfNull(queryString);

        return Parse(queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            return equals < 0
                ? KeyValuePair.Create(Decode(pair), "")
                : KeyValuePair.Create(Decode(pair[..equals]), Decode(pair[(equals + 1)..]));
        }));
    }

    public static ContentQuery Parse(IEnumerable<KeyValuePair<string, string>> parameters) => Parse(parameters, keepSources: false);

    // keepSources records the parameter of each condition in FilterSources, so that the engine's errors name it.
    public static ContentQuery Parse(IEnumerable<KeyValuePair<string, string>> parameters, bool keepSources)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        List<ValidationError> errors = [];
        List<FilterEntry> filters = [];
        Dictionary<string, string> single = new(StringComparer.Ordinal);

        foreach (var (key, value) in parameters)
        {
            if (key == Filter || key.StartsWith(Filter + "[", StringComparison.Ordinal))
            {
                if (filters.Any(filter => filter.Key == key))
                {
                    errors.Add(new(key, QueryErrorCodes.InvalidParameter, $"'{key}' is given more than once."));
                }
                else if (Tokens(key) is { } tokens)
                {
                    filters.Add(new(key, tokens, value));
                }
                else
                {
                    errors.Add(new(key, QueryErrorCodes.InvalidParameter, $"'{key}' is not in the form filter[path][operator]."));
                }
            }
            else if (!single.TryAdd(key, value))
            {
                errors.Add(new(key, QueryErrorCodes.InvalidParameter, $"'{key}' is given more than once."));
            }
        }

        var depth = Depth(single, errors);
        var query = new ContentQuery
        {
            Filter = filters.Count == 0 ? null : Node(filters, 0, 0, keepSources, errors),
            Sort = Sort(single, errors),
            Page = Page(single, errors),
            Cursor = Take(single, "cursor"),
            Select = Paths(single, "fields", errors, allowAll: false),
            Populate = Paths(single, "populate", errors, allowAll: true),
            PopulateDepth = depth,
            Status = Choice<QueryStatus?>(single, "status", errors, ("published", QueryStatus.Published), ("draft", QueryStatus.Draft)),
            Locale = Locale(single, errors),
            Fallback = Choice<LocaleFallback?>(single, "fallback", errors, ("none", LocaleFallback.None)) ?? LocaleFallback.Chain,
            Site = Site(single, errors),
            RichTextFormat = Choice<string>(single, "richText", errors, ("json", "json"), (RichTextFormats.Html, RichTextFormats.Html), (RichTextFormats.Markdown, RichTextFormats.Markdown)),
        };

        if (query.Cursor is not null && query.Page is { Number: not null } or { Offset: not null })
        {
            errors.Add(new("cursor", QueryErrorCodes.InvalidParameter, "A cursor cannot be combined with a page number or an offset."));
        }

        errors.AddRange(single.Keys.Select(key => new ValidationError(key, QueryErrorCodes.InvalidParameter, $"'{key}' is not a query parameter.")));

        return errors.Count > 0 ? throw new ValidationException(errors) : query;
    }

    private sealed record FilterEntry(string Key, string[] Tokens, string Value);

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    // filter[a][b] as ["a", "b"], or null when the brackets do not balance. Nothing may stand between them.
    private static string[]? Tokens(string key)
    {
        List<string> tokens = [];
        var position = Filter.Length;
        while (position < key.Length)
        {
            var close = key.IndexOf(']', position);
            if (key[position] != '[' || close < 0)
            {
                return null;
            }

            tokens.Add(key[(position + 1)..close]);
            position = close + 1;
        }

        return tokens.Count > 0 ? [.. tokens] : null;
    }

    // The conditions at one level, in the order they first appear; several of them are an implicit And.
    private static ContentFilter? Node(IReadOnlyList<FilterEntry> entries, int offset, int depth, bool keepSources, List<ValidationError> errors)
    {
        if (depth > MaxFilterDepth)
        {
            errors.Add(new(entries[0].Key, QueryErrorCodes.LimitExceeded, $"The filter nests deeper than the {MaxFilterDepth} levels allowed."));
            return null;
        }

        List<ContentFilter> nodes = [];
        HashSet<string> done = new(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var left = entry.Tokens.Length - offset;
            var token = left > 0 ? entry.Tokens[offset] : "";

            if (token is OrToken or AndToken && left >= 3 && IsIndex(entry.Tokens[offset + 1]))
            {
                if (done.Add(token))
                {
                    var group = Group(entries.Where(item => IsGroup(item, offset, token)).ToList(), offset, depth, keepSources, errors);
                    nodes.Add(token == OrToken ? new Or(group) : Single(group) ?? new And(group));
                }
            }
            else if (token == NotToken && left >= 3)
            {
                if (done.Add(token) && Node(entries.Where(item => IsNot(item, offset)).ToList(), offset + 1, depth + 1, keepSources, errors) is { } inner)
                {
                    nodes.Add(new Not(inner));
                }
            }
            else if (Leaf(entry, offset, errors) is { } leaf)
            {
                if (keepSources)
                {
                    FilterSources.Remember(leaf, entry.Key);
                }

                nodes.Add(leaf);
            }
        }

        return Single(nodes) ?? (nodes.Count > 0 ? new And(nodes) : null);
    }

    private static ContentFilter? Single(List<ContentFilter> nodes) => nodes.Count == 1 ? nodes[0] : null;

    private static bool IsIndex(string token) => token.Length is > 0 and <= 6 && token.All(char.IsAsciiDigit);

    private static bool IsGroup(FilterEntry entry, int offset, string token) =>
        entry.Tokens.Length - offset >= 3 && entry.Tokens[offset] == token && IsIndex(entry.Tokens[offset + 1]);

    private static bool IsNot(FilterEntry entry, int offset) =>
        entry.Tokens.Length - offset >= 3 && entry.Tokens[offset] == NotToken;

    private static List<ContentFilter> Group(List<FilterEntry> entries, int offset, int depth, bool keepSources, List<ValidationError> errors) =>
        entries
            .GroupBy(entry => int.Parse(entry.Tokens[offset + 1], CultureInfo.InvariantCulture))
            .OrderBy(group => group.Key)
            .Select(group => Node(group.ToList(), offset + 2, depth + 1, keepSources, errors))
            .OfType<ContentFilter>()
            .ToList();

    private static FieldFilter? Leaf(FilterEntry entry, int offset, List<ValidationError> errors)
    {
        if (entry.Tokens.Length - offset != 2)
        {
            errors.Add(new(entry.Key, QueryErrorCodes.InvalidParameter, $"'{entry.Key}' is not in the form filter[path][operator]."));
            return null;
        }

        var path = entry.Tokens[offset];
        if (!IsPath(path))
        {
            errors.Add(new(entry.Key, QueryErrorCodes.UnknownField, $"'{path}' is not a path: each part is letters, digits and underscores."));
            return null;
        }

        var name = entry.Tokens[offset + 1];
        if (!Operators.TryGetValue(name, out var filterOperator))
        {
            errors.Add(new(entry.Key, QueryErrorCodes.OperatorNotAllowed, $"'{name}' is not a filter operator."));
            return null;
        }

        switch (filterOperator)
        {
            case FilterOperator.In or FilterOperator.NotIn:
                return new(path, filterOperator, SplitList(entry.Value));
            case FilterOperator.Between:
                var range = SplitList(entry.Value);
                if (range.Count == 2)
                {
                    return new(path, filterOperator, range);
                }

                errors.Add(new(entry.Key, QueryErrorCodes.InvalidValue, "between needs two values separated by a comma."));
                return null;
            case FilterOperator.IsNull or FilterOperator.IsNotNull:
                return NullCheck(entry, path, filterOperator, errors);
            default:
                return new(path, filterOperator, entry.Value);
        }
    }

    private static FieldFilter? NullCheck(FilterEntry entry, string path, FilterOperator filterOperator, List<ValidationError> errors)
    {
        switch (entry.Value)
        {
            case "" or "true":
                return new(path, filterOperator);
            case "false":
                return new(path, filterOperator == FilterOperator.IsNull ? FilterOperator.IsNotNull : FilterOperator.IsNull);
            default:
                errors.Add(new(entry.Key, QueryErrorCodes.InvalidValue, "A null check takes true or false."));
                return null;
        }
    }

    private static List<object?> SplitList(string value)
    {
        List<object?> items = [];
        StringBuilder item = new();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\' && index + 1 < value.Length)
            {
                item.Append(value[++index]);
            }
            else if (character == ',')
            {
                items.Add(item.ToString());
                item.Clear();
            }
            else
            {
                item.Append(character);
            }
        }

        items.Add(item.ToString());
        return items;
    }

    // Field names and JSON keys alike, so JsonScalar.CheckField can never throw on what passes here.
    private static bool IsPath(string path) => path.Split('.').All(JsonScalar.IsField);

    private static string? Take(Dictionary<string, string> single, string key) =>
        single.Remove(key, out var value) ? value : null;

    private static List<SortField> Sort(Dictionary<string, string> single, List<ValidationError> errors)
    {
        List<SortField> sort = [];
        if (Take(single, "sort") is not { } text)
        {
            return sort;
        }

        foreach (var token in text.Split(','))
        {
            var descending = token.StartsWith('-');
            var path = descending ? token[1..] : token;
            if (!IsPath(path))
            {
                errors.Add(new("sort", QueryErrorCodes.UnknownField, $"'{token}' is not a sort key."));
            }
            else if (sort.Any(field => field.Path == path))
            {
                errors.Add(new("sort", QueryErrorCodes.NotSortable, $"'{path}' is named more than once."));
            }
            else
            {
                sort.Add(new(path, descending));
            }
        }

        return sort;
    }

    private static PageRequest? Page(Dictionary<string, string> single, List<ValidationError> errors)
    {
        var number = Number(single, "page", 1, errors);
        var pageSize = Number(single, "pageSize", 1, errors);
        var offset = Number(single, "offset", 0, errors);
        var limit = Number(single, "limit", 1, errors);
        var withTotal = Take(single, "withTotal");

        if ((number is not null || pageSize is not null) && (offset is not null || limit is not null))
        {
            errors.Add(new("offset", QueryErrorCodes.InvalidParameter, "page and pageSize cannot be combined with offset and limit."));
        }

        if (withTotal is not (null or "true" or "false"))
        {
            errors.Add(new("withTotal", QueryErrorCodes.InvalidValue, "withTotal takes true or false."));
        }

        return number is null && pageSize is null && offset is null && limit is null && withTotal is null
            ? null
            : new PageRequest { Number = number, Offset = offset, Size = pageSize ?? limit, WithTotal = withTotal != "false" };
    }

    private static int? Number(Dictionary<string, string> single, string key, int minimum, List<ValidationError> errors)
    {
        if (Take(single, key) is not { } text)
        {
            return null;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum)
        {
            return number;
        }

        errors.Add(new(key, QueryErrorCodes.InvalidValue, $"{key} takes a whole number of at least {minimum}."));
        return null;
    }

    private static List<string> Paths(Dictionary<string, string> single, string key, List<ValidationError> errors, bool allowAll)
    {
        List<string> paths = [];
        if (Take(single, key) is not { } text)
        {
            return paths;
        }

        foreach (var path in text.Split(','))
        {
            if (IsPath(path) || (allowAll && path == "*"))
            {
                paths.Add(path);
            }
            else
            {
                errors.Add(new(key, QueryErrorCodes.UnknownField, $"'{path}' is not a path: each part is letters, digits and underscores."));
            }
        }

        return paths;
    }

    private static int? Depth(Dictionary<string, string> single, List<ValidationError> errors)
    {
        var hasPopulate = single.ContainsKey("populate");
        var depth = Number(single, "depth", 1, errors);
        if (depth is not null && !hasPopulate)
        {
            errors.Add(new("depth", QueryErrorCodes.InvalidParameter, "depth needs populate."));
        }

        return depth;
    }

    private static T? Choice<T>(Dictionary<string, string> single, string key, List<ValidationError> errors, params (string Name, T Value)[] choices)
    {
        if (Take(single, key) is not { } text)
        {
            return default;
        }

        foreach (var (name, value) in choices)
        {
            if (name == text)
            {
                return value;
            }
        }

        errors.Add(new(key, QueryErrorCodes.InvalidValue, $"{key} takes {string.Join(" or ", choices.Select(choice => choice.Name))}."));
        return default;
    }

    private static string? Locale(Dictionary<string, string> single, List<ValidationError> errors)
    {
        var locale = Take(single, "locale");
        if (locale is not null && (locale.Length is 0 or > StorageNames.LocaleLength || !locale.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            errors.Add(new("locale", QueryErrorCodes.InvalidValue, "locale takes a BCP 47 tag such as fr-CA."));
            return null;
        }

        return locale;
    }

    private static SiteId? Site(Dictionary<string, string> single, List<ValidationError> errors)
    {
        if (Take(single, "site") is not { } text)
        {
            return null;
        }

        if (Guid.TryParseExact(text, "D", out var id))
        {
            return new SiteId(id);
        }

        errors.Add(new("site", QueryErrorCodes.InvalidValue, "site takes a site id."));
        return null;
    }
}
