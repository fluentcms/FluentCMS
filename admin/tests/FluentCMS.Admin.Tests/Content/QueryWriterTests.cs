using System.Globalization;
using System.Text.Json;
using FluentCMS.Admin.Content;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

// clients/filter-cases.json, which FluentCMS.Client and @fluentcms/client also write: the admin builds each case from a
// path, an operator and values, as its list filters do, and writes the same query string.
public sealed class QueryWriterTests
{
    private static readonly Lazy<JsonDocument> Cases = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "filter-cases.json"))));

    public static TheoryData<string> FilterNames => [.. Items("filters").Select(Name)];

    public static TheoryData<string> QueryNames => [.. Items("queries").Select(item => item.GetProperty("name").GetString()!)];

    [Theory]
    [MemberData(nameof(FilterNames))]
    public void AFilterCaseIsWrittenAsTheOtherClientsWriteIt(string name)
    {
        var item = Items("filters").Single(candidate => Name(candidate) == name);

        new ContentQuery { Filter = Filter(item) }.ToQueryString().ShouldBe(item.GetProperty("query").GetString());
    }

    [Theory]
    [MemberData(nameof(QueryNames))]
    public void AQueryCaseIsWrittenAsTheOtherClientsWriteIt(string name)
    {
        var item = Items("queries").Single(candidate => candidate.GetProperty("name").GetString() == name);

        Query(item.GetProperty("query")).ToQueryString().ShouldBe(item.GetProperty("expect").GetString());
    }

    [Fact]
    public void EveryCaseOfTheFileIsRun()
    {
        FilterNames.Count.ShouldBe(Items("filters").Count());
        Items("filters").Count().ShouldBeGreaterThan(40);
        QueryNames.Count.ShouldBe(Items("queries").Count());
    }

    [Fact]
    public void AnEmptyListOrGroupIsRefused()
    {
        Should.Throw<ArgumentException>(() => new ContentQuery { Filter = new FieldFilter("title", FilterOperator.In, Array.Empty<string>()) }.ToQueryString());
        Should.Throw<ArgumentException>(() => new ContentQuery { Filter = new Or() }.ToQueryString());
    }

    private static JsonElement.ArrayEnumerator Items(string name) => Cases.Value.RootElement.GetProperty(name).EnumerateArray();

    private static string Name(JsonElement item) => $"{item.GetProperty("field").GetString()} {item.GetProperty("query").GetString()}";

    private static ContentQuery Query(JsonElement query) => new()
    {
        Filter = query.TryGetProperty("filter", out var filter) ? Node(filter) : null,
        Sort = query.TryGetProperty("sort", out var sort)
            ? [.. sort.EnumerateArray().Select(field => new SortField(field.GetProperty("path").GetString()!, field.GetProperty("call").GetString() == "descending"))]
            : [],
        Page = query.TryGetProperty("page", out var page) ? page.GetInt32() : null,
        PageSize = query.TryGetProperty("pageSize", out var pageSize) ? pageSize.GetInt32() : null,
        Cursor = query.TryGetProperty("cursor", out var cursor) ? cursor.GetString() : null,
        Fields = query.TryGetProperty("fields", out var fields) ? [.. fields.EnumerateArray().Select(value => value.GetString()!)] : [],
        Populate = query.TryGetProperty("populate", out var populate) ? [.. populate.EnumerateArray().Select(value => value.GetString()!)] : [],
        Depth = query.TryGetProperty("depth", out var depth) ? depth.GetInt32() : null,
        Status = query.TryGetProperty("status", out var status) ? Enum.Parse<ContentStatus>(status.GetString()!, ignoreCase: true) : null,
        Locale = query.TryGetProperty("locale", out var locale) ? locale.GetString() : null,
    };

    private static ContentFilter Node(JsonElement node) =>
        node.TryGetProperty("and", out var every) ? new And([.. every.EnumerateArray().Select(Node)])
        : node.TryGetProperty("or", out var any) ? new Or([.. any.EnumerateArray().Select(Node)])
        : node.TryGetProperty("not", out var negated) ? new Not(Node(negated))
        : Filter(node);

    // A key of a JSON field is a segment of the path; a list operator takes the arguments as its list.
    private static FieldFilter Filter(JsonElement item)
    {
        var path = item.GetProperty("path").GetString()!;
        if (item.TryGetProperty("key", out var key))
        {
            path = $"{path}.{key.GetString()}";
        }

        var of = item.TryGetProperty("of", out var generic) ? generic.GetString() : null;
        var args = item.GetProperty("args").EnumerateArray().Select(value => Value(value, of)).ToList();
        var filterOperator = Operator(item.GetProperty("call").GetString()!);
        object? operand = filterOperator switch
        {
            FilterOperator.In or FilterOperator.NotIn or FilterOperator.Between => args,
            FilterOperator.IsNull or FilterOperator.IsNotNull => null,
            _ => args.Single(),
        };

        return new FieldFilter(path, filterOperator, operand);
    }

    private static FilterOperator Operator(string call) => call switch
    {
        "eq" => FilterOperator.Equal,
        "ne" => FilterOperator.NotEqual,
        "in" => FilterOperator.In,
        "notIn" => FilterOperator.NotIn,
        "lt" => FilterOperator.LessThan,
        "lte" => FilterOperator.LessThanOrEqual,
        "gt" => FilterOperator.GreaterThan,
        "gte" => FilterOperator.GreaterThanOrEqual,
        "between" => FilterOperator.Between,
        "contains" => FilterOperator.Contains,
        "startsWith" => FilterOperator.StartsWith,
        "isNull" => FilterOperator.IsNull,
        "isNotNull" => FilterOperator.IsNotNull,
        "hasBlock" => FilterOperator.HasBlock,
        _ => throw new InvalidOperationException($"'{call}' is not a call of the cases."),
    };

    private static object Value(JsonElement value, string? of) => of switch
    {
        "DateTimeOffset" => DateTimeOffset.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        "DateOnly" => DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        "long" => value.GetInt64(),
        "decimal" => value.GetDecimal(),
        _ => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetInt64(),
            _ => value.GetString()!,
        },
    };
}
