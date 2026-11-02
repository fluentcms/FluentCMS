using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Sites;
using Shouldly;
using Xunit;
using static FluentCMS.Content.FilterOperator;

namespace FluentCMS.Data.Tests.Query;

public class BracketGrammarTests
{
    // The grammar of spec 4.4, one line of it per parameter.
    private const string SpecExample =
        "filter[category][eq]=news"
        + "&filter[author.name][contains]=amir"
        + "&filter[or][0][title][startsWith]=A&filter[or][1][title][startsWith]=B"
        + "&filter[sections][hasBlock]=hero"
        + "&sort=-publishedOn,title"
        + "&page=2&pageSize=20"
        + "&fields=title,slug,author.name"
        + "&populate=author,tags"
        + "&status=published"
        + "&locale=fr-CA&fallback=none"
        + "&richText=html"
        + "&withTotal=false";

    private static ContentQuery Parse(string query) => BracketQueryParser.Parse(query);

    private static ValidationError Rejected(string query) =>
        Should.Throw<ValidationException>(() => Parse(query)).Errors.Single();

    [Fact]
    public void TheExampleOfTheSpecificationParsesIntoTheAst()
    {
        var query = Parse(SpecExample);

        query.Filter.ShouldBe(new And(
            new FieldFilter("category", Equal, "news"),
            new FieldFilter("author.name", Contains, "amir"),
            new Or(new FieldFilter("title", StartsWith, "A"), new FieldFilter("title", StartsWith, "B")),
            new FieldFilter("sections", HasBlock, "hero")));
        query.Sort.ShouldBe([new SortField("publishedOn", Descending: true), new SortField("title")]);
        query.Page.ShouldBe(new PageRequest { Number = 2, Size = 20, WithTotal = false });
        query.Select.ShouldBe(["title", "slug", "author.name"]);
        query.Populate.ShouldBe(["author", "tags"]);
        query.Status.ShouldBe(QueryStatus.Published);
        query.Locale.ShouldBe("fr-CA");
        query.Fallback.ShouldBe(LocaleFallback.None);
        query.RichTextFormat.ShouldBe("html");
    }

    [Fact]
    public void TheExampleOfTheSpecificationRoundTrips()
    {
        BracketQueryWriter.Write(Parse(SpecExample)).ShouldBe(SpecExample);
    }

    [Theory]
    [InlineData("filter[title][eq]=a")]
    [InlineData("filter[title][ne]=a")]
    [InlineData("filter[title][in]=a,b")]
    [InlineData("filter[title][nin]=a,b")]
    [InlineData("filter[views][lt]=1")]
    [InlineData("filter[views][lte]=1")]
    [InlineData("filter[views][gt]=1")]
    [InlineData("filter[views][gte]=1")]
    [InlineData("filter[views][between]=1,2")]
    [InlineData("filter[title][contains]=a")]
    [InlineData("filter[title][startsWith]=a")]
    [InlineData("filter[title][null]=true")]
    [InlineData("filter[title][notNull]=true")]
    [InlineData("filter[sections][hasBlock]=hero")]
    [InlineData("filter[not][title][eq]=a")]
    [InlineData("filter[not][not][title][eq]=a")]
    [InlineData("filter[or][0][a][eq]=1&filter[or][1][b][eq]=2")]
    [InlineData("filter[or][0][a][eq]=1&filter[or][0][b][eq]=2&filter[or][1][c][eq]=3")]
    [InlineData("filter[not][or][0][a][eq]=1&filter[not][or][1][b][eq]=2")]
    [InlineData("filter[or][0][and][0][or][0][a][eq]=1&filter[or][0][and][0][or][1][b][eq]=2&filter[or][0][and][1][or][0][c][eq]=3&filter[or][0][and][1][or][1][d][eq]=4")]
    [InlineData("filter[a][eq]=1&filter[b][eq]=2")]
    [InlineData("filter[and][0][or][0][a][eq]=1&filter[and][0][or][1][b][eq]=2&filter[and][1][or][0][c][eq]=3&filter[and][1][or][1][d][eq]=4")]
    [InlineData("filter[and][0][a][eq]=1&filter[and][1][a][eq]=2")]
    [InlineData("filter[title][eq]=a%20b%26c%3Dd%2Be")]
    [InlineData("filter[title][in]=a%5C,b,c%5C%5Cd")]
    [InlineData("sort=-a,b")]
    [InlineData("page=2&pageSize=20")]
    [InlineData("pageSize=2")]
    [InlineData("offset=20&limit=20")]
    [InlineData("pageSize=10&cursor=abc-_")]
    [InlineData("fields=a,b.c")]
    [InlineData("populate=*&depth=2")]
    [InlineData("populate=a,b.c")]
    [InlineData("status=draft")]
    [InlineData("locale=fr-CA&fallback=none")]
    [InlineData("site=0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f")]
    [InlineData("richText=markdown")]
    [InlineData("withTotal=false")]
    [InlineData("")]
    public void TheGrammarRoundTrips(string query)
    {
        var parsed = Parse(query);

        BracketQueryWriter.Write(parsed).ShouldBe(query);
        ShouldBeTheSameQuery(Parse(BracketQueryWriter.Write(parsed)), parsed);
    }

    [Fact]
    public void AHandBuiltAstRoundTrips()
    {
        var query = new ContentQuery
        {
            Filter = new Or(
                new And(new FieldFilter("a", In, new object?[] { "x,y", 2L }), new Not(new FieldFilter("b", IsNull))),
                new FieldFilter("c", Between, new object?[] { 1.5m, 2.5m }),
                new FieldFilter("d", Equal, true),
                new FieldFilter("e", Equal, new DateOnly(2026, 1, 2))),
            Sort = [new SortField("a", Descending: true)],
            Page = new PageRequest { Offset = 5, Size = 10 },
            Site = new SiteId(Guid.Parse("0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f")),
        };

        var read = Parse(BracketQueryWriter.Write(query));

        read.Filter.ShouldBe(new Or(
            new And(new FieldFilter("a", In, new object?[] { "x,y", "2" }), new Not(new FieldFilter("b", IsNull))),
            new FieldFilter("c", Between, new object?[] { "1.5", "2.5" }),
            new FieldFilter("d", Equal, "true"),
            new FieldFilter("e", Equal, "2026-01-02")));
        read.Page.ShouldBe(query.Page);
        read.Site.ShouldBe(query.Site);
    }

    [Theory]
    [InlineData("?filter[title][eq]=a")]
    [InlineData("filter%5Btitle%5D%5Beq%5D=a")]
    public void AQuestionMarkAndAnEncodedKeyAreAccepted(string query) =>
        Parse(query).Filter.ShouldBe(new FieldFilter("title", Equal, "a"));

    [Fact]
    public void APlusIsASpace() =>
        Parse("filter[title][eq]=a+b").Filter.ShouldBe(new FieldFilter("title", Equal, "a b"));

    [Fact]
    public void ListOperandsSplitOnCommasWithEscapes() =>
        Parse(@"filter[title][in]=a\,b,c\\d").Filter.ShouldBe(new FieldFilter("title", In, new object?[] { "a,b", @"c\d" }));

    [Fact]
    public void GroupsAreOrderedByIndexNotByAppearance() =>
        Parse("filter[or][1][a][eq]=1&filter[or][0][b][eq]=2").Filter.ShouldBe(new Or(new FieldFilter("b", Equal, "2"), new FieldFilter("a", Equal, "1")));

    [Fact]
    public void SeveralConditionsInAGroupAreAnAnd() =>
        Parse("filter[or][0][a][eq]=1&filter[or][0][b][eq]=2&filter[or][1][c][eq]=3").Filter.ShouldBe(new Or(
            new And(new FieldFilter("a", Equal, "1"), new FieldFilter("b", Equal, "2")),
            new FieldFilter("c", Equal, "3")));

    [Fact]
    public void AGroupNameIsAFieldWhereTheFormSaysSo()
    {
        Parse("filter[not][eq]=1").Filter.ShouldBe(new FieldFilter("not", Equal, "1"));
        Parse("filter[or][eq]=1").Filter.ShouldBe(new FieldFilter("or", Equal, "1"));
    }

    [Fact]
    public void ANullCheckTakesTrueOrFalse()
    {
        Parse("filter[a][null]").Filter.ShouldBe(new FieldFilter("a", IsNull));
        Parse("filter[a][null]=false").Filter.ShouldBe(new FieldFilter("a", IsNotNull));
        Parse("filter[a][notNull]=false").Filter.ShouldBe(new FieldFilter("a", IsNull));
    }

    [Theory]
    [InlineData("filter", "filter")]
    [InlineData("filter[a]=1", "filter[a]")]
    [InlineData("filter[a][b][c]=1", "filter[a][b][c]")]
    [InlineData("filter[a][eq=1", "filter[a][eq")]
    [InlineData("filter[a]x[eq]=1", "filter[a]x[eq]")]
    [InlineData("filter[or][0][a]=1", "filter[or][0][a]")]
    public void AMalformedFilterKeyIsRejected(string query, string path)
    {
        var error = Rejected(query);

        error.Code.ShouldBe(QueryErrorCodes.InvalidParameter);
        error.Path.ShouldBe(path);
    }

    [Fact]
    public void AnUnknownOperatorIsRejected() =>
        Rejected("filter[a][like]=1").Code.ShouldBe(QueryErrorCodes.OperatorNotAllowed);

    [Theory]
    [InlineData("filter[a][between]=1")]
    [InlineData("filter[a][between]=1,2,3")]
    [InlineData("filter[a][null]=maybe")]
    [InlineData("page=0")]
    [InlineData("page=abc")]
    [InlineData("page=-1")]
    [InlineData("pageSize=0")]
    [InlineData("offset=-1")]
    [InlineData("limit=0")]
    [InlineData("depth=0&populate=a")]
    [InlineData("status=archived")]
    [InlineData("fallback=chain")]
    [InlineData("richText=pdf")]
    [InlineData("withTotal=maybe")]
    [InlineData("locale=fr_CA")]
    [InlineData("locale=a-very-long-locale-tag")]
    [InlineData("site=notaguid")]
    public void ABadValueIsRejected(string query) =>
        Rejected(query).Code.ShouldBe(QueryErrorCodes.InvalidValue);

    [Theory]
    [InlineData("page=1&offset=1")]
    [InlineData("pageSize=1&limit=1")]
    [InlineData("cursor=x&page=1")]
    [InlineData("cursor=x&offset=1")]
    [InlineData("depth=2")]
    [InlineData("nothing=1")]
    [InlineData("sort=a&sort=b")]
    [InlineData("filter[a][eq]=1&filter[a][eq]=2")]
    public void AnInvalidCombinationIsRejected(string query) =>
        Rejected(query).Code.ShouldBe(QueryErrorCodes.InvalidParameter);

    [Theory]
    [InlineData("sort=my-field")]
    [InlineData("sort=")]
    [InlineData("sort=a,")]
    [InlineData("fields=my-field")]
    [InlineData("fields=*")]
    [InlineData("populate=my-field")]
    public void ABadPathIsRejected(string query) =>
        Rejected(query).Code.ShouldBe(QueryErrorCodes.UnknownField);

    [Fact]
    public void ASortKeyNamedTwiceIsRejected() =>
        Rejected("sort=a,-a").Code.ShouldBe(QueryErrorCodes.NotSortable);

    [Fact]
    public void AFilterNestedTooDeeplyIsRejected()
    {
        var key = "filter" + string.Concat(Enumerable.Repeat("[not]", 20)) + "[a][eq]";

        Rejected($"{key}=1").Code.ShouldBe(QueryErrorCodes.LimitExceeded);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var errors = Should.Throw<ValidationException>(() => Parse("filter[a][like]=1&page=0&status=x&nothing=1")).Errors;

        errors.Select(error => error.Path).ShouldBe(["filter[a][like]", "page", "status", "nothing"], ignoreOrder: true);
    }

    private static void ShouldBeTheSameQuery(ContentQuery actual, ContentQuery expected)
    {
        actual.Filter.ShouldBe(expected.Filter);
        actual.Sort.ShouldBe(expected.Sort);
        actual.Page.ShouldBe(expected.Page);
        actual.Cursor.ShouldBe(expected.Cursor);
        actual.Select.ShouldBe(expected.Select);
        actual.Populate.ShouldBe(expected.Populate);
        actual.PopulateDepth.ShouldBe(expected.PopulateDepth);
        actual.Status.ShouldBe(expected.Status);
        actual.Locale.ShouldBe(expected.Locale);
        actual.Fallback.ShouldBe(expected.Fallback);
        actual.Site.ShouldBe(expected.Site);
        actual.RichTextFormat.ShouldBe(expected.RichTextFormat);
    }
}
