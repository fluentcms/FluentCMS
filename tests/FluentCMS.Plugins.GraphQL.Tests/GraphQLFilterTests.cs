using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.GraphQL.Tests;

// A filter input builds the AST the REST bracket parser builds for the same conditions, node for node, so the content
// service cannot tell the two surfaces apart.
[Collection(BlogTests.Name)]
public sealed class GraphQLFilterTests(BlogHost blog)
{
    [Theory]
    [InlineData("""{ title: { eq: "A" } }""", "filter[title][eq]=A")]
    [InlineData("""{ title: { ne: "A", contains: "b", startsWith: "c" } }""", "filter[title][ne]=A&filter[title][contains]=b&filter[title][startsWith]=c")]
    [InlineData("""{ slug: { in: ["a", "b"], nin: ["c"] } }""", "filter[slug][in]=a,b&filter[slug][nin]=c")]
    [InlineData("""{ publishedOn: { gt: "2026-01-01T00:00:00Z", lte: "2026-12-31T00:00:00Z" } }""", "filter[publishedOn][gt]=2026-01-01T00:00:00Z&filter[publishedOn][lte]=2026-12-31T00:00:00Z")]
    [InlineData("""{ publishedOn: { between: ["2026-01-01T00:00:00Z", "2026-12-31T00:00:00Z"] } }""", "filter[publishedOn][between]=2026-01-01T00:00:00Z,2026-12-31T00:00:00Z")]
    [InlineData("""{ publishedOn: { null: true } }""", "filter[publishedOn][null]=true")]
    [InlineData("""{ publishedOn: { null: false } }""", "filter[publishedOn][null]=false")]
    [InlineData("""{ publishedOn: { notNull: false } }""", "filter[publishedOn][notNull]=false")]
    [InlineData("""{ version: { gte: 2, lt: 9 } }""", "filter[version][gte]=2&filter[version][lt]=9")]
    [InlineData("""{ status: { in: [DRAFT, MODIFIED] } }""", "filter[status][in]=draft,modified")]
    [InlineData("""{ id: { eq: "0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f" } }""", "filter[id][eq]=0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f")]
    [InlineData("""{ author: { eq: "0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f" } }""", "filter[author][eq]=0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f")]
    [InlineData("""{ author: { where: { name: { contains: "amir" } } } }""", "filter[author.name][contains]=amir")]
    [InlineData("""{ seo: { metaTitle: { startsWith: "T" } } }""", "filter[seo.metaTitle][startsWith]=T")]
    [InlineData("""{ sections: { hasBlock: "hero" } }""", "filter[sections][hasBlock]=hero")]
    [InlineData("""{ title: { eq: "A" }, slug: { eq: "a" } }""", "filter[title][eq]=A&filter[slug][eq]=a")]
    [InlineData("""{ or: [{ title: { startsWith: "A" } }, { title: { startsWith: "B" } }] }""", "filter[or][0][title][startsWith]=A&filter[or][1][title][startsWith]=B")]
    [InlineData("""{ and: [{ title: { eq: "A" } }, { or: [{ slug: { eq: "a" } }, { slug: { eq: "b" } }] }] }""", "filter[and][0][title][eq]=A&filter[and][1][or][0][slug][eq]=a&filter[and][1][or][1][slug][eq]=b")]
    [InlineData("""{ not: { title: { eq: "A" }, slug: { eq: "a" } } }""", "filter[not][title][eq]=A&filter[not][slug][eq]=a")]
    [InlineData("""{ not: { author: { where: { not: { name: { eq: "A" } } } } } }""", "filter[not][not][author.name][eq]=A")]
    public void AFilterInputBuildsTheAstOfTheBracketParser(string input, string brackets)
    {
        var filter = FilterReader.Read(Utf8GraphQLParser.Syntax.ParseValueLiteral(input), Article());

        filter.ShouldNotBeNull();
        filter.ShouldBe(BracketQueryParser.Parse(brackets).Filter);
    }

    [Fact]
    public void AnEnumValueOfASelectIsItsOption()
    {
        var article = Article();
        var options = article.Find("category")!.OptionsByName;

        var filter = FilterReader.Read(Utf8GraphQLParser.Syntax.ParseValueLiteral("{ category: { eq: NEWS, in: [NEWS, VIDEO] } }"), article);

        filter.ShouldBe(BracketQueryParser.Parse($"filter[category][eq]={options["NEWS"]}&filter[category][in]={options["NEWS"]},{options["VIDEO"]}").Filter);
        options.Keys.ShouldBe(["NEWS", "OPINION", "VIDEO"], ignoreOrder: true);
    }

    [Fact]
    public void AFilterWithNoConditionIsNoFilter()
    {
        FilterReader.Read(Utf8GraphQLParser.Syntax.ParseValueLiteral("{ }"), Article()).ShouldBeNull();
        FilterReader.Read(Utf8GraphQLParser.Syntax.ParseValueLiteral("{ title: null, or: [] }"), Article()).ShouldBeNull();
        FilterReader.Read(NullValueNode.Default, Article()).ShouldBeNull();
        Should.Throw<ValidationException>(() => FilterReader.Read(Utf8GraphQLParser.Syntax.ParseValueLiteral("""{ publishedOn: { between: ["2026-01-01"] } }"""), Article()))
            .Errors.Single().Code.ShouldBe("FCMS2204");
    }

    [Fact]
    public void ASortInputIsTheSortOfRest()
    {
        var paths = new Dictionary<string, string> { ["PUBLISHED_ON"] = "publishedOn", ["TITLE"] = "title" };

        var sort = SortReader.Read(Utf8GraphQLParser.Syntax.ParseValueLiteral("[{ field: PUBLISHED_ON, direction: DESC }, { field: TITLE }]"), paths);

        sort.ShouldBe(BracketQueryParser.Parse("sort=-publishedOn,title").Sort);
    }

    [Theory]
    [InlineData("article", "Article")]
    [InlineData("site-settings", "SiteSettings")]
    [InlineData("publishedOn", "PublishedOn")]
    public void ANameOfTheContentSchemaIsAGraphQLTypeName(string name, string expected) => GraphQLNames.Pascal(name).ShouldBe(expected);

    [Theory]
    [InlineData("publishedOn", "PUBLISHED_ON")]
    [InlineData("release-notes", "RELEASE_NOTES")]
    [InlineData("News", "NEWS")]
    [InlineData("4k", "_4K")]
    [InlineData("c#", null)]
    [InlineData("true", null)]
    [InlineData("", null)]
    public void AnOptionIsAnEnumValueWhenItCanBeAName(string option, string? expected) => GraphQLNames.EnumValue(option).ShouldBe(expected);

    private TypeModel Article() =>
        blog.Host.Services.GetRequiredService<ContentModel>().ContentTypes.Single(type => type.SchemaName == "article");
}
