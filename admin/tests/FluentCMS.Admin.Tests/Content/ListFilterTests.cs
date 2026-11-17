using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Lists;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Lists.FilterOperator;

namespace FluentCMS.Admin.Tests.Content;

// The operators each core field type offers in a list, and the filters of a list's address (spec 4.4).
public sealed class ListFilterTests
{
    public static TheoryData<string, FilterOperand, FilterOperator[]> CoreTypes => new()
    {
        { "text", FilterOperand.Text, [Equal, NotEqual, Contains, StartsWith, In, NotIn, IsNull, IsNotNull] },
        { "email", FilterOperand.Text, [Equal, NotEqual, Contains, StartsWith, In, NotIn, IsNull, IsNotNull] },
        { "url", FilterOperand.Text, [Equal, NotEqual, Contains, StartsWith, In, NotIn, IsNull, IsNotNull] },
        { "slug", FilterOperand.Text, [Equal, NotEqual, Contains, StartsWith, In, NotIn, IsNull, IsNotNull] },
        { "longText", FilterOperand.Text, [Contains, StartsWith, IsNull, IsNotNull] },
        { "richText", FilterOperand.Text, [IsNull, IsNotNull] },
        { "json", FilterOperand.Text, [IsNull, IsNotNull] },
        { "integer", FilterOperand.WholeNumber, [Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Between, In, NotIn, IsNull, IsNotNull] },
        { "decimal", FilterOperand.DecimalNumber, [Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Between, In, NotIn, IsNull, IsNotNull] },
        { "date", FilterOperand.Date, [Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Between, IsNull, IsNotNull] },
        { "dateTime", FilterOperand.DateTime, [LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Between, IsNull, IsNotNull] },
        { "boolean", FilterOperand.Boolean, [Equal, NotEqual, IsNull, IsNotNull] },
        { "select", FilterOperand.Option, [Equal, NotEqual, In, NotIn, IsNull, IsNotNull] },
        { "relation", FilterOperand.Id, [Equal, In, IsNull, IsNotNull] },
        { "blocks", FilterOperand.Text, [HasBlock, IsNull, IsNotNull] },
    };

    [Theory]
    [MemberData(nameof(CoreTypes))]
    public void EachCoreFieldTypeOffersItsOperatorsAndNoOther(string type, FilterOperand operand, FilterOperator[] operators)
    {
        var filter = new ListFilterRegistry().Find(Field("value", type)).ShouldNotBeNull();

        filter.FieldType.ShouldBe(type);
        filter.Operand.ShouldBe(operand);
        filter.Operators.ShouldBe(operators);
    }

    [Fact]
    public void AComponentAPluginTypeWithoutOperatorsAndAHiddenFieldAreOfferedNoFilter()
    {
        var filters = new ListFilterRegistry();

        filters.Find(Field("seo", "component")).ShouldBeNull();
        filters.Find(Field("hero", "media")).ShouldBeNull();
        filters.Find(Field("title", "text") with { Hidden = true }).ShouldBeNull();
    }

    [Fact]
    public void AListOfOptionsTestsItsMembersAndARelationListKeepsItsOperators()
    {
        var filters = new ListFilterRegistry();

        filters.Find(Field("labels", "select") with { Multiple = true })!.Operators.ShouldBe([Contains, In, NotIn, IsNull, IsNotNull]);
        filters.Find(Field("tags", "relation") with { Multiple = true })!.Operators.ShouldBe([Equal, In, IsNull, IsNotNull]);
    }

    // The seam A-18 fills from an admin half's manifest.
    [Fact]
    public void APluginFieldTypeNamesItsOperators()
    {
        var filters = new ListFilterRegistry();

        filters.Add("media", FilterOperand.Id, Equal, In, Equal);

        var media = filters.Find(Field("hero", "media")).ShouldNotBeNull();
        media.Operand.ShouldBe(FilterOperand.Id);
        media.Operators.ShouldBe([Equal, In]);
        Should.Throw<ArgumentException>(() => filters.Add("rating", FilterOperand.WholeNumber));
    }

    [Fact]
    public void TheAddressKeepsTheFiltersTheListOffersWithOperandsOfTheirType()
    {
        var fields = Fields();

        var filters = ListFilters.Parse(
            "?filter[title][contains]=a%20b&filter[rank][between]=1,5&filter[day][gt]=2026-03-01&filter[category][in]=news,video"
            + "&filter[featured][eq]=true&filter[author][eq]=0192f2a4-1111-7000-8000-000000000001&filter[body][notNull]=true&filter[at][lt]=2026-01-01T09%3A00%3A00%2B02%3A00",
            fields);

        filters.ShouldBe(
        [
            new FieldFilter("title", Contains, "a b"),
            new FieldFilter("rank", Between, new List<object> { 1L, 5L }),
            new FieldFilter("day", GreaterThan, new DateOnly(2026, 3, 1)),
            new FieldFilter("category", In, new List<object> { "news", "video" }),
            new FieldFilter("featured", Equal, true),
            new FieldFilter("author", Equal, new Guid("0192f2a4-1111-7000-8000-000000000001")),
            new FieldFilter("body", IsNotNull),
            new FieldFilter("at", LessThan, new DateTimeOffset(2026, 1, 1, 7, 0, 0, TimeSpan.Zero)),
        ]);
    }

    [Theory]
    [InlineData("filter[secret][eq]=x")]
    [InlineData("filter[title][gt]=x")]
    [InlineData("filter[title][nope]=x")]
    [InlineData("filter[rank][eq]=abc")]
    [InlineData("filter[rank][between]=1")]
    [InlineData("filter[category][eq]=sport")]
    [InlineData("filter[day][eq]=03/01/2026")]
    [InlineData("filter[author][in]=1,2")]
    [InlineData("filter[title][eq]=")]
    [InlineData("filter[seo.metaTitle][eq]=x")]
    [InlineData("filter[title]=x")]
    public void AFilterTheListDoesNotOfferOrWhoseOperandDoesNotReadIsDropped(string query) =>
        ListFilters.Parse(query, Fields()).ShouldBeEmpty();

    [Fact]
    public void TheSameFilterTwiceIsKeptOnce() =>
        ListFilters.Parse("filter[title][eq]=a&filter[title][eq]=a&filter[title][eq]=b", Fields()).Count.ShouldBe(2);

    [Fact]
    public void AChangedFilterLeavesThePageAndKeepsTheRestOfTheAddress()
    {
        var address = ListFilters.Address(
            "content/article?sort=title&page=3&filter[title][eq]=old&q=old&columns=status",
            [new FieldFilter("title", Contains, "a,b"), new FieldFilter("category", In, new List<object> { "news", "a,b" })],
            "  new search  ");

        address.ShouldBe("content/article?sort=title&columns=status&filter[title][contains]=a,b&filter[category][in]=news,a%5C,b&q=new%20search");
        ListFilters.Parse(address[address.IndexOf('?', StringComparison.Ordinal)..], Fields())[0].Value.ShouldBe("a,b");
        ListFilters.Address("content/article?page=2&q=x", [], " ").ShouldBe("content/article");
    }

    [Fact]
    public void ASearchIsTrimmedAndOneTooLongForTheApiIsDropped()
    {
        ListFilters.ParseSearch("  tour ").ShouldBe("tour");
        ListFilters.ParseSearch("   ").ShouldBeNull();
        ListFilters.ParseSearch(new string('a', 256)).ShouldNotBeNull();
        ListFilters.ParseSearch(new string('a', 257)).ShouldBeNull();
        ListFilters.SearchOf("?sort=title&q=release%20no").ShouldBe("release no");
    }

    [Fact]
    public void TheReadOfAListWithFiltersSearchAndEveryLocaleWritesThemAsTheApiTakesThem()
    {
        var type = Type(Fields().Select(field => field.Field).ToArray());
        var view = ListView.For(type, new SchemaDescription { Types = [type], Components = [], Blocks = [] }, new ListCellRegistry());
        var shown = view.Choose(["status"]);

        var query = view.Query(shown, ListView.DefaultSort, 1, 25, [new FieldFilter("title", Contains, "a"), new FieldFilter("category", Equal, "news")], "tour", everyLocale: true);

        query.ToQueryString().ShouldBe("filter[title][contains]=a&filter[category][eq]=news&q=tour&sort=-updatedAt&page=1&pageSize=25&fields=title,status,scheduledPublishAt,scheduledUnpublishAt&locale=all&withTotal=true");
        view.Searchable.ShouldBeTrue();
        view.Filters.Select(field => field.Name).ShouldBe(["title", "rank", "day", "at", "category", "featured", "author", "body"]);
    }

    private static List<FilterField> Fields()
    {
        var registry = new ListFilterRegistry();
        var category = Field("category", "select") with { Settings = new JsonObject { ["options"] = new JsonArray("news", "video", "opinion", "a,b") } };
        SchemaFieldDescription[] fields =
        [
            Field("title", "text") with { Searchable = true },
            Field("rank", "integer"),
            Field("day", "date"),
            Field("at", "dateTime"),
            category,
            Field("featured", "boolean"),
            Field("author", "relation"),
            Field("body", "richText"),
            Field("secret", "text") with { Hidden = true },
            Field("seo", "component"),
        ];
        return [.. fields.Select(field => registry.Find(field) is { } filter ? new FilterField(field, filter) : null).OfType<FilterField>()];
    }

    private static SchemaTypeDescription Type(SchemaFieldDescription[] fields) => new()
    {
        Name = "article",
        Kind = "collection",
        TitleField = "title",
        Fields = fields,
    };

    private static SchemaFieldDescription Field(string name, string type) => new() { Name = name, Type = type, Validation = new FieldValidationDescription() };
}
