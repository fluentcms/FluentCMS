using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

// Bad input is a validation error with a code and never reaches the database, so it is never a 500.
public sealed class BadInputTests : IDisposable
{
    private readonly QueryDatabase _database = new();

    public BadInputTests() => _database.AddArticle("Alpha");

    public void Dispose() => _database.Dispose();

    private async Task<ValidationError> Rejected(string queryString, IContentQueryAccess? access = null)
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync($"locale=en&{queryString}", access));
        return exception.Errors.Single();
    }

    private async Task<ValidationError> RejectedFilter(string path, FilterOperator filterOperator, object? value = null, IContentQueryAccess? access = null)
    {
        var query = new ContentQuery { Locale = "en", Filter = new FieldFilter(path, filterOperator, value) };
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine(access).ExecuteAsync("article", query, TestContext.Current.CancellationToken));
        return exception.Errors.Single();
    }

    [Theory]
    [InlineData("title")]
    [InlineData("views")]
    [InlineData("metadata")]
    [InlineData("labels")]
    [InlineData("seo")]
    [InlineData("faq")]
    [InlineData("author")]
    [InlineData("seo.metaTitle")]
    public async Task HasBlockIsRejectedOnAnythingButABlocksField(string path) =>
        (await Rejected($"filter[{path}][hasBlock]=hero")).Code.ShouldBe(QueryErrorCodes.OperatorNotAllowed);

    [Fact]
    public async Task HasBlockOnAJsonKeyIsRejected() =>
        (await Rejected("filter[metadata.audience][hasBlock]=hero")).Code.ShouldBe(QueryErrorCodes.OperatorNotAllowed);

    [Fact]
    public async Task HasBlockOnABlockThatTheFieldDoesNotAllowIsRejected() =>
        (await Rejected("filter[sections][hasBlock]=video")).Code.ShouldBe(QueryErrorCodes.InvalidValue);

    [Theory]
    [InlineData("filter[metadata.my-key][eq]=x")]
    [InlineData("filter[metadata.my key][eq]=x")]
    [InlineData("filter[metadata.$..x][eq]=x")]
    [InlineData("filter[metadata.a'b][eq]=x")]
    [InlineData("filter[][eq]=x")]
    [InlineData("filter[a..b][eq]=x")]
    public void AJsonKeyThatIsNotAPlainNameFailsAtTheParser(string query)
    {
        var exception = Should.Throw<ValidationException>(() => BracketQueryParser.Parse(query));

        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Fact]
    public async Task AJsonKeyThatIsNotAPlainNameFailsInTheCompilerForOtherFrontEnds()
    {
        (await RejectedFilter("metadata.my-key", FilterOperator.Equal, "x")).Code.ShouldBe(QueryErrorCodes.UnknownField);
        (await RejectedFilter("metadata.my-key", FilterOperator.Contains, "x")).Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Theory]
    [InlineData("filter[title][lt]=a")]
    [InlineData("filter[title][between]=a,b")]
    [InlineData("filter[views][contains]=1")]
    [InlineData("filter[views][startsWith]=1")]
    [InlineData("filter[featured][lt]=true")]
    [InlineData("filter[featured][in]=true,false")]
    [InlineData("filter[body][eq]=x")]
    [InlineData("filter[sections][eq]=x")]
    [InlineData("filter[author][ne]=00000000-0000-0000-0000-000000000000")]
    [InlineData("filter[hero][null]=true")]
    [InlineData("filter[faq][null]=true")]
    [InlineData("filter[id][lt]=00000000-0000-0000-0000-000000000000")]
    [InlineData("filter[locale][eq]=en")]
    [InlineData("filter[metadata.audience][lt]=1")]
    [InlineData("filter[sections.hero.heading][lt]=1")]
    public async Task AnOperatorTheFieldTypeDoesNotAllowIsRejected(string query) =>
        (await Rejected(query)).Code.ShouldBe(QueryErrorCodes.OperatorNotAllowed);

    [Theory]
    [InlineData("filter[nothing][eq]=x")]
    [InlineData("filter[secret][eq]=x")]
    [InlineData("filter[title.deeper][eq]=x")]
    [InlineData("filter[author.nothing][eq]=x")]
    [InlineData("filter[author.secret][eq]=x")]
    [InlineData("filter[seo.nothing][eq]=x")]
    [InlineData("filter[sections.hero.nothing][eq]=x")]
    [InlineData("filter[sections.hero.internal][eq]=x")]
    [InlineData("filter[sections.video.heading][eq]=x")]
    public async Task AnUnknownOrHiddenPathIsRejected(string query) =>
        (await Rejected(query)).Code.ShouldBe(QueryErrorCodes.UnknownField);

    [Theory]
    [InlineData("filter[views][eq]=abc")]
    [InlineData("filter[views][in]=1,abc")]
    [InlineData("filter[views][between]=1,abc")]
    [InlineData("filter[price][gt]=abc")]
    [InlineData("filter[featured][eq]=maybe")]
    [InlineData("filter[issued][eq]=tomorrow")]
    [InlineData("filter[publishedOn][gt]=tomorrow")]
    [InlineData("filter[contact][eq]=notanemail")]
    [InlineData("filter[author][eq]=notaguid")]
    [InlineData("filter[id][eq]=notaguid")]
    [InlineData("filter[status][eq]=notastatus")]
    [InlineData("filter[status][eq]=1")]
    [InlineData("filter[metadata][eq]=x")]
    [InlineData("filter[metadata.a.b][eq]=x")]
    [InlineData("filter[sections.heading][eq]=x")]
    [InlineData("filter[sections.hero.note][null]=true")]
    [InlineData("filter[sections.hero.level][eq]=abc")]
    public async Task AValueThatDoesNotFitIsRejected(string query) =>
        (await Rejected(query)).Code.ShouldBe(QueryErrorCodes.InvalidValue);

    [Fact]
    public async Task EveryProblemIsReportedAtOnce()
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync(
            "locale=en&filter[nothing][eq]=x&filter[views][eq]=abc&sort=sections&fields=nothing&populate=title&pageSize=1000"));

        exception.Errors.Select(error => error.Code).ShouldBe(
            [QueryErrorCodes.UnknownField, QueryErrorCodes.InvalidValue, QueryErrorCodes.UnknownField, QueryErrorCodes.NotARelation, QueryErrorCodes.NotSortable, QueryErrorCodes.LimitExceeded],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("eq")]
    [InlineData("in")]
    [InlineData("between")]
    public async Task AnOperandIsRequiredInAST(string name)
    {
        var filterOperator = name switch { "eq" => FilterOperator.Equal, "in" => FilterOperator.In, _ => FilterOperator.Between };

        (await RejectedFilter("views", filterOperator)).Code.ShouldBe(QueryErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task ABetweenWithoutTwoValuesIsRejectedInTheAst() =>
        (await RejectedFilter("views", FilterOperator.Between, new object[] { 1L, 2L, 3L })).Code.ShouldBe(QueryErrorCodes.InvalidValue);

    [Theory]
    [InlineData("sort=nothing")]
    [InlineData("sort=sections")]
    [InlineData("sort=metadata")]
    [InlineData("sort=author")]
    [InlineData("sort=author.name")]
    [InlineData("sort=labels")]
    [InlineData("sort=secret")]
    [InlineData("sort=status")]
    public async Task AFieldThatCannotBeSortedIsRejected(string query) =>
        (await Rejected(query)).Code.ShouldBe(QueryErrorCodes.NotSortable);

    [Theory]
    [InlineData("fields=nothing")]
    [InlineData("fields=secret")]
    [InlineData("fields=title.deeper")]
    [InlineData("fields=author.nothing")]
    [InlineData("fields=author.secret")]
    [InlineData("fields=metadata.audience")]
    [InlineData("fields=author.company.owner.company.name")]
    public async Task ASelectionOfAPathThatCannotBeReadIsRejected(string query) =>
        (await Rejected(query)).Code.ShouldBe(QueryErrorCodes.UnknownField);

    [Theory]
    [InlineData("populate=nothing", QueryErrorCodes.UnknownField)]
    [InlineData("populate=secret", QueryErrorCodes.UnknownField)]
    [InlineData("populate=title", QueryErrorCodes.NotARelation)]
    [InlineData("populate=seo", QueryErrorCodes.NotARelation)]
    [InlineData("populate=hero.deeper", QueryErrorCodes.NotARelation)]
    public async Task APopulationOfAnythingButARelationIsRejected(string query, string code) =>
        (await Rejected(query)).Code.ShouldBe(code);

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("limit=101")]
    [InlineData("populate=*&depth=4")]
    public async Task AValueOverACapIsRejected(string query) =>
        (await Rejected(query)).Code.ShouldBe(QueryErrorCodes.LimitExceeded);

    [Fact]
    public async Task TooManyFilterNodesAreRejected()
    {
        var options = new ContentQueryOptions { MaxFilterNodes = 3 };
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&filter[views][in]=1,2,3,4", options: options));

        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
    }

    [Fact]
    public async Task TooManyRelationHopsAreRejected() =>
        (await Rejected("filter[author.company.owner.company.name][eq]=x")).Code.ShouldBe(QueryErrorCodes.LimitExceeded);

    [Fact]
    public async Task APageTooFarIntoTheListIsRejected() =>
        (await Rejected("page=2147483647&pageSize=100")).Code.ShouldBe(QueryErrorCodes.LimitExceeded);

    [Theory]
    [InlineData("notbase64!!!")]
    [InlineData("e30")]
    [InlineData("eyJpZCI6Im5vdGFndWlkIn0")]
    [InlineData("W10")]
    [InlineData("bm90anNvbg")]
    public async Task AMalformedCursorIsRejected(string cursor) =>
        (await Rejected($"cursor={cursor}")).Code.ShouldBe(QueryErrorCodes.InvalidCursor);

    [Fact]
    public async Task ACursorOfAnotherOrderIsRejected()
    {
        _database.AddArticle("Beta");
        var result = await _database.RunAsync("locale=en&sort=title&limit=1");

        (await Rejected($"sort=-title&limit=1&cursor={result.NextCursor}")).Code.ShouldBe(QueryErrorCodes.InvalidCursor);
    }

    [Fact]
    public async Task ACursorWithAPageNumberIsRejectedInTheAst()
    {
        var query = new ContentQuery { Locale = "en", Cursor = "x", Page = new PageRequest { Number = 2 } };

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine().ExecuteAsync("article", query, TestContext.Current.CancellationToken));

        exception.Errors.Select(error => error.Path).ShouldContain("cursor");
    }

    [Theory]
    [InlineData("")]
    [InlineData("locale=all")]
    public async Task ALocalizedTypeNeedsOneLocale(string query)
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync(query));

        exception.Errors.Single().Path.ShouldBe("locale");
    }

    [Fact]
    public async Task ANonLocalizedTypeIgnoresTheLocale()
    {
        _database.AddAuthor("Ann");

        (await _database.RunAsync("locale=fr", type: "author")).Items.Single()["name"].ShouldBe("Ann");
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("seo")]
    [InlineData("hero")]
    public async Task AnUnknownContentTypeIsRejected(string type) =>
        (await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("", type: type))).Errors.Single().Path.ShouldBe("type");

    [Fact]
    public async Task AFilterOrSortOnAFieldThePrincipalCannotReadIsRejected()
    {
        var access = new QueryAccess();
        access.DeniedFields.Add("article.views");
        access.DeniedFields.Add("author.name");
        access.DeniedFields.Add("article.createdBy");
        access.DeniedTypes.Add("company");

        (await Rejected("filter[views][eq]=1", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
        (await Rejected("filter[author.name][eq]=x", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
        (await Rejected("filter[createdBy][eq]=00000000-0000-0000-0000-000000000000", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
        (await Rejected("filter[author.company.name][eq]=x", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
        (await Rejected("sort=views", access)).Code.ShouldBe(QueryErrorCodes.NotSortable);
        (await Rejected("fields=views", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Fact]
    public async Task ARowRuleLimitsTheQueryAndMayNameAFieldTheClientCannotRead()
    {
        _database.AddArticle("Beta", new() { ["secret"] = "mine" });
        var access = new QueryAccess();
        access.DeniedFields.Add("article.views");
        access.RowRules["article"] = new FieldFilter("secret", FilterOperator.Equal, "mine");

        var result = await _database.RunAsync("locale=en", access);

        result.Items.Single()["title"].ShouldBe("Beta");
        result.Total.ShouldBe(1);
        (await Rejected("filter[secret][eq]=mine", access)).Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Fact]
    public async Task ARowRuleOnARelationTargetLimitsARelationPath()
    {
        var ann = _database.AddAuthor("Ann", secret: "hidden");
        var article = _database.AddArticle("With author");
        _database.Relate("article", article, "author", "author", ann);

        var access = new QueryAccess();
        access.RowRules["author"] = new FieldFilter("secret", FilterOperator.Equal, "other");

        (await _database.RunAsync("locale=en&filter[author.name][eq]=Ann", access)).Items.ShouldBeEmpty();

        access.RowRules["author"] = new FieldFilter("secret", FilterOperator.Equal, "hidden");

        (await _database.RunAsync("locale=en&filter[author.name][eq]=Ann", access)).Items.Single()["title"].ShouldBe("With author");
    }

    [Fact]
    public void TheEngineIsInTheContainer() =>
        _database.Services.GetService<ContentQueryEngine>().ShouldNotBeNull();
}
