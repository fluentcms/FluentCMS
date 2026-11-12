using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace FluentCMS.Client.Tests;

// clients/filter-cases.json, which @fluentcms/client's tests read too, built with this client's field types.
internal static class FilterCases
{
    private static readonly Lazy<JsonDocument> Document = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "filter-cases.json"))));

    public static IEnumerable<JsonElement> Filters => Document.Value.RootElement.GetProperty("filters").EnumerateArray();

    public static IEnumerable<JsonElement> Queries => Document.Value.RootElement.GetProperty("queries").EnumerateArray();

    public static bool ReachesTheApi(JsonElement item) => !item.TryGetProperty("api", out var api) || api.GetBoolean();

    public static FieldFilter Filter(JsonElement item) => (FieldFilter)Call(item);

    public static ContentQuery Query(JsonElement item)
    {
        var query = item.GetProperty("query");
        return new ContentQuery
        {
            Filter = query.TryGetProperty("filter", out var filter) ? Node(filter) : null,
            Sort = query.TryGetProperty("sort", out var sort) ? [.. sort.EnumerateArray().Select(field => (SortField)Call(field))] : [],
            Page = query.TryGetProperty("page", out var page) ? page.GetInt32() : null,
            PageSize = query.TryGetProperty("pageSize", out var pageSize) ? pageSize.GetInt32() : null,
            Cursor = query.TryGetProperty("cursor", out var cursor) ? cursor.GetString() : null,
            Fields = query.TryGetProperty("fields", out var fields) ? [.. fields.EnumerateArray().Select(value => value.GetString()!)] : [],
            Populate = query.TryGetProperty("populate", out var populate) ? [.. populate.EnumerateArray().Select(value => value.GetString()!)] : [],
            Depth = query.TryGetProperty("depth", out var depth) ? depth.GetInt32() : null,
            Status = query.TryGetProperty("status", out var status) ? Enum.Parse<QueryStatus>(status.GetString()!, ignoreCase: true) : null,
            Locale = query.TryGetProperty("locale", out var locale) ? locale.GetString() : null,
        };
    }

    private static ContentFilter Node(JsonElement node) =>
        node.TryGetProperty("and", out var every) ? new And([.. every.EnumerateArray().Select(Node)])
        : node.TryGetProperty("or", out var any) ? new Or([.. any.EnumerateArray().Select(Node)])
        : node.TryGetProperty("not", out var negated) ? new Not(Node(negated))
        : Filter(node);

    // The field type the case names, with the method of its call and the arguments in its JSON.
    private static object Call(JsonElement item)
    {
        var name = item.GetProperty("field").GetString()!;
        var generic = item.TryGetProperty("of", out var of);
        var type = typeof(QueryField).Assembly.GetType($"FluentCMS.Client.{name}{(generic ? "`1" : "")}", throwOnError: true)!;
        if (generic)
        {
            type = type.MakeGenericType(Of(of.GetString()!));
        }

        var field = Activator.CreateInstance(type, item.GetProperty("path").GetString()!)!;
        if (item.TryGetProperty("key", out var key))
        {
            field = ((JsonField)field).Key(key.GetString()!);
        }

        var method = char.ToUpperInvariant(item.GetProperty("call").GetString()![0]) + item.GetProperty("call").GetString()![1..];
        var args = item.TryGetProperty("args", out var list) ? list.EnumerateArray().ToList() : [];
        foreach (var candidate in field.GetType().GetMethods().Where(candidate => candidate.Name == method))
        {
            if (Arguments(candidate.GetParameters(), args) is { } values)
            {
                return candidate.Invoke(field, values)!;
            }
        }

        throw new InvalidOperationException($"{name}.{method} takes no arguments like {string.Join(", ", args)}.");
    }

    private static object?[]? Arguments(ParameterInfo[] parameters, List<JsonElement> args)
    {
        try
        {
            if (parameters is [{ ParameterType: { IsGenericType: true } listType }] && listType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                var itemType = listType.GetGenericArguments()[0];
                var items = Array.CreateInstance(itemType, args.Count);
                for (var index = 0; index < args.Count; index++)
                {
                    items.SetValue(Value(args[index], itemType), index);
                }

                return [items];
            }

            return parameters.Length == args.Count ? [.. parameters.Select((parameter, index) => Value(args[index], parameter.ParameterType))] : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static object Value(JsonElement value, Type type) =>
        type == typeof(string) ? value.GetString()!
        : type == typeof(long) ? value.GetInt64()
        : type == typeof(decimal) ? value.GetDecimal()
        : type == typeof(bool) ? value.GetBoolean()
        : type == typeof(Guid) ? Guid.ParseExact(value.GetString()!, "D")
        : type == typeof(DateOnly) ? DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : type == typeof(DateTimeOffset) ? DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        : throw new InvalidOperationException($"No case value is a {type.Name}.");

    private static Type Of(string name) => name switch
    {
        "long" => typeof(long),
        "decimal" => typeof(decimal),
        "bool" => typeof(bool),
        "DateOnly" => typeof(DateOnly),
        "DateTimeOffset" => typeof(DateTimeOffset),
        _ => throw new InvalidOperationException($"'{name}' is not a generic argument of the cases."),
    };
}
