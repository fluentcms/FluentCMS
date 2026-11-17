using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
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

    // A list's address holds its filters in the same grammar: each case the list offers on a field of its type is read from
    // the address and written back as the clients write it; one the list does not offer, such as a key of a JSON field or a
    // field of a block, is not read.
    [Theory]
    [MemberData(nameof(FilterNames))]
    public void TheAddressOfAListReadsACaseItOffersAndWritesItAsTheOtherClients(string name)
    {
        var item = Items("filters").Single(candidate => Name(candidate) == name);
        var query = item.GetProperty("query").GetString()!;
        var expected = Filter(item);

        var read = ListFilters.Parse(query, AddressFields);

        if (Offered(expected))
        {
            read.Single().Path.ShouldBe(expected.Path);
            read.Single().Operator.ShouldBe(expected.Operator);
            new ContentQuery { Filter = read.Single() }.ToQueryString().ShouldBe(query);
        }
        else
        {
            read.ShouldBeEmpty();
        }
    }

    [Fact]
    public void TheAddressOfAListReadsMostCases() =>
        Items("filters").Count(item => Offered(Filter(item))).ShouldBeGreaterThan(30);

    // The fields of the cases' paths with the field type each case's class stands for; media as an admin half would name it.
    private static readonly IReadOnlyList<FilterField> AddressFields = CaseFields();

    private static bool Offered(FieldFilter filter) =>
        AddressFields.Find(filter.Path) is { } field && field.Filter.Operators.Contains(filter.Operator);

    private static List<FilterField> CaseFields()
    {
        var registry = new ListFilterRegistry();
        registry.Add("media", FilterOperand.Id, FilterOperator.Equal, FilterOperator.In);
        SchemaFieldDescription[] fields =
        [
            Field("title", "text"), Field("slug", "slug"), Field("videoUrl", "url"), Field("body", "richText"),
            Field("category", "select", options: ["news", "video", "opinion"]), Field("publishedOn", "dateTime"),
            Field("author", "relation"), Field("tags", "relation", multiple: true), Field("hero", "media"), Field("sections", "blocks", options: ["hero", "quote", "gallery"]),
            Field("rating", "integer"), Field("price", "decimal"), Field("day", "date"), Field("featured", "boolean"),
            Field("summary", "longText"), Field("labels", "select", multiple: true, options: ["a", "b"]), Field("meta", "json"),
        ];
        return [.. fields.Select(field => registry.Find(field) is { } filter ? new FilterField(field, filter) : null).OfType<FilterField>()];
    }

    private static SchemaFieldDescription Field(string name, string type, bool multiple = false, string[]? options = null) => new()
    {
        Name = name,
        Type = type,
        Multiple = multiple,
        Validation = new FieldValidationDescription(),
        Settings = options is null ? [] : new JsonObject { [type == "blocks" ? "blocks" : "options"] = new JsonArray([.. options.Select(option => (JsonNode)option)]) },
    };

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
