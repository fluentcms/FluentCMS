using System.Buffers.Text;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
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
    [InlineData("filter[status][eq]=")]
    [InlineData("filter[status][in]=published,")]
    [InlineData("filter[id][eq]=")]
    [InlineData("filter[createdAt][gt]=")]
    [InlineData("filter[version][eq]=")]
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

    [Fact]
    public async Task APageOrOffsetThatSkipsMoreThanTheMaximumIsRejectedAndPointsAtCursors()
    {
        new ContentQueryOptions().MaxOffset.ShouldBe(10_000);
        await _database.RunAsync("locale=en&offset=10000");
        (await Rejected("offset=10001")).Path.ShouldBe("offset");

        var options = new ContentQueryOptions { MaxOffset = 4 };
        await _database.RunAsync("locale=en&offset=4", options: options);
        await _database.RunAsync("locale=en&page=3&pageSize=2", options: options);
        await _database.RunAsync("locale=en&limit=100", options: options);

        foreach (var (query, path) in new[] { ("offset=5", "offset"), ("page=2&pageSize=5", "page"), ("page=4&pageSize=2", "page") })
        {
            var error = (await Should.ThrowAsync<ValidationException>(() => _database.RunAsync($"locale=en&{query}", options: options))).Errors.Single();
            (error.Path, error.Code).ShouldBe((path, QueryErrorCodes.LimitExceeded), query);
            error.Message.ShouldContain("cursor");
        }
    }

    [Theory]
    [InlineData("notbase64!!!")]
    [InlineData("e30")]
    [InlineData("eyJpZCI6Im5vdGFndWlkIn0")]
    [InlineData("W10")]
    [InlineData("bm90anNvbg")]
    public async Task AMalformedCursorIsRejected(string cursor) =>
        (await Rejected($"cursor={cursor}")).Code.ShouldBe(QueryErrorCodes.InvalidCursor);

    [Fact]
    public async Task ACursorHoldingInvalidTextIsRejected()
    {
        var utf16 = Base64Url.EncodeToString("""{"id":"\ud800"}"""u8);
        var utf8 = Base64Url.EncodeToString([.. "{\""u8, 0xFF, .. "\":1}"u8]);

        (await Rejected($"cursor={utf16}")).Code.ShouldBe(QueryErrorCodes.InvalidCursor);
        (await Rejected($"cursor={utf8}")).Code.ShouldBe(QueryErrorCodes.InvalidCursor);
    }

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
    public async Task EveryLocaleIsRejectedOnANonLocalizedType() =>
        (await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=all", type: "author"))).Errors.Single().Path.ShouldBe("locale");

    [Theory]
    [InlineData("filter[latest.title][eq]=x")]
    [InlineData("filter[latest][notNull]=true&filter[latest.views][gt]=1")]
    [InlineData("filter[company.owner.latest.title][eq]=x")]
    [InlineData("populate=latest")]
    [InlineData("populate=*")]
    public async Task AGoingIntoALocalizedTargetNeedsALocale(string query)
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync(query, type: "author"));

        exception.Errors.Single().Path.ShouldBe("locale");
    }

    [Fact]
    public async Task ANonLocalizedSourceReachesALocalizedTargetInTheGivenLocale()
    {
        var author = _database.AddAuthor("Ann");
        _database.Relate("author", author, "latest", "article", _database.Context.Documents.Single(document => document.Type == "article").DocumentId);

        (await _database.RunAsync("locale=en&filter[latest.title][eq]=Alpha", type: "author")).Items.Single()["name"].ShouldBe("Ann");

        var entry = (await _database.RunAsync("locale=en&populate=latest", type: "author")).Items.Single();
        entry["latest"].ShouldBeOfType<ContentDocument>()["title"].ShouldBe("Alpha");
    }

    [Theory]
    [InlineData("status=draft")]
    [InlineData("status=draft&populate=author")]
    [InlineData("status=draft&filter[author.name][eq]=x")]
    public async Task DraftsAreClosedByDefault(string query)
    {
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync($"locale=en&{query}"));

        exception.Errors.ShouldAllBe(error => error.Code == QueryErrorCodes.DraftNotAllowed && error.Path == "status");
    }

    [Fact]
    public async Task ADraftQueryNeedsAccessToTheType()
    {
        _database.AddArticle("Draft", state: ContentState.Draft);
        var access = new QueryAccess();

        (await _database.RunAsync("locale=en&status=draft", access)).Items.Single()["title"].ShouldBe("Draft");

        access.DeniedDrafts.Add("article");

        (await Rejected("status=draft", access)).Code.ShouldBe(QueryErrorCodes.DraftNotAllowed);
    }

    [Theory]
    [InlineData("status=draft&filter[author.name][eq]=x")]
    [InlineData("status=draft&filter[not][author.name][eq]=x")]
    [InlineData("status=draft&populate=author")]
    [InlineData("status=draft&populate=*")]
    public async Task ADraftQueryNeedsAccessToEveryRelationTarget(string query)
    {
        var access = new QueryAccess();
        access.DeniedDrafts.Add("author");

        (await Rejected(query, access)).Code.ShouldBe(QueryErrorCodes.DraftNotAllowed);
    }

    [Fact]
    public async Task APublishedQueryDoesNotNeedDraftAccess()
    {
        var access = new QueryAccess();
        access.DeniedDrafts.Add("article");
        access.DeniedDrafts.Add("author");

        (await _database.RunAsync("locale=en&populate=author&filter[not][author.name][eq]=x", access)).Items.Single()["title"].ShouldBe("Alpha");
    }

    [Fact]
    public async Task ADraftReadThroughARowRuleOnARelationIsNotTheClientsRead()
    {
        _database.AddArticle("Draft", state: ContentState.Draft);
        var access = new QueryAccess();
        access.DeniedDrafts.Add("author");
        access.RowRules["article"] = new Not(new FieldFilter("author.name", FilterOperator.Equal, "x"));

        (await _database.RunAsync("locale=en&status=draft", access)).Items.Single()["title"].ShouldBe("Draft");
    }

    [Fact]
    public async Task TooManyPopulatedEntriesAreRejected()
    {
        var options = new ContentQueryOptions { MaxPopulatedEntries = 3 };
        var article = _database.Context.Documents.Single(document => document.Type == "article").DocumentId;
        for (var position = 0; position < 4; position++)
        {
            _database.Relate("article", article, "tags", "tag", _database.AddTag($"Tag {position}"), position);
        }

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&populate=tags", options: options));
        var error = exception.Errors.Single();

        error.Code.ShouldBe(QueryErrorCodes.LimitExceeded);
        error.Path.ShouldBe("populate");

        (await _database.RunAsync("locale=en&populate=tags", options: new ContentQueryOptions { MaxPopulatedEntries = 4 })).Items.Single()["tags"]
            .ShouldBeAssignableTo<IReadOnlyList<ContentDocument>>()!.Count.ShouldBe(4);
        (await _database.RunAsync("locale=en", options: options)).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ThePopulatedEntriesOfEveryLevelCount()
    {
        var article = _database.Context.Documents.Single(document => document.Type == "article").DocumentId;
        var author = _database.AddAuthor("Ann");
        _database.Relate("article", article, "author", "author", author);
        _database.Relate("author", author, "company", "company", _database.AddCompany("Acme"));

        await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&populate=author.company", options: new ContentQueryOptions { MaxPopulatedEntries = 1 }));

        (await _database.RunAsync("locale=en&populate=author.company", options: new ContentQueryOptions { MaxPopulatedEntries = 2 })).Items.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AnEmptyLogicalNodeIsRejected(int shape)
    {
        ContentFilter filter = shape switch
        {
            0 => new And(),
            1 => new Or(),
            2 => new Not(new And()),
            _ => new Or(new FieldFilter("title", FilterOperator.Equal, "x"), new Not(new Or())),
        };
        var query = new ContentQuery { Locale = "en", Filter = filter };

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine().ExecuteAsync("article", query, TestContext.Current.CancellationToken));
        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.InvalidValue);

        Should.Throw<ValidationException>(() => BracketQueryWriter.Write(query)).Errors.Single().Code.ShouldBe(QueryErrorCodes.InvalidValue);
    }

    [Theory]
    [InlineData(FilterOperator.In)]
    [InlineData(FilterOperator.NotIn)]
    [InlineData(FilterOperator.Between)]
    public void TheWriterRejectsAListOperandWithNoValues(FilterOperator filterOperator) =>
        Should.Throw<ValidationException>(() => BracketQueryWriter.Write(new ContentQuery { Filter = new FieldFilter("title", filterOperator, Array.Empty<object?>()) }))
            .Errors.Single().Code.ShouldBe(QueryErrorCodes.InvalidValue);

    [Fact]
    public async Task AFilterChainFarBeyondTheLimitIsRejectedWithoutWalkingIt()
    {
        ContentFilter filter = new FieldFilter("title", FilterOperator.Equal, "x");
        for (var index = 0; index < 1_000_000; index++)
        {
            filter = new Not(filter);
        }

        var query = new ContentQuery { Locale = "en", Filter = filter };

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine().ExecuteAsync("article", query, TestContext.Current.CancellationToken));
        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);

        Should.Throw<ValidationException>(() => BracketQueryWriter.Write(query)).Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
    }

    [Fact]
    public async Task ADeepChainWithinTheNodeCapIsStillRejected()
    {
        ContentFilter filter = new FieldFilter("title", FilterOperator.Equal, "x");
        for (var index = 0; index < 80; index++)
        {
            filter = new Not(filter);
        }

        var query = new ContentQuery { Locale = "en", Filter = filter };

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine().ExecuteAsync("article", query, TestContext.Current.CancellationToken));
        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
    }

    [Fact]
    public async Task AWideFilterStopsAtTheCap()
    {
        var query = new ContentQuery
        {
            Locale = "en",
            Filter = new And([.. Enumerable.Range(0, 200).Select(index => new FieldFilter("nothing", FilterOperator.Equal, index))]),
        };

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.Engine().ExecuteAsync("article", query, TestContext.Current.CancellationToken));

        exception.Errors.Single().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
    }

    [Fact]
    public async Task ANegativeOperatorThroughARelationMustBeOneTheFieldTypeAllows()
    {
        (await Rejected("filter[author.company][ne]=00000000-0000-0000-0000-000000000000")).Code.ShouldBe(QueryErrorCodes.OperatorNotAllowed);
        (await Rejected("filter[author.company][nin]=00000000-0000-0000-0000-000000000000")).Code.ShouldBe(QueryErrorCodes.OperatorNotAllowed);
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
    public async Task AFilterOnARelationDoesNotMatchATargetOfATypeThePrincipalCannotRead()
    {
        var ann = _database.AddAuthor("Ann");
        var article = _database.AddArticle("With author");
        _database.Relate("article", article, "author", "author", ann);

        var access = new QueryAccess();
        access.DeniedTypes.Add("author");

        (await _database.RunAsync($"locale=en&filter[author][eq]={ann}", access)).Items.ShouldBeEmpty();
        (await _database.RunAsync($"locale=en&filter[author][in]={ann}", access)).Items.ShouldBeEmpty();
        (await _database.RunAsync("locale=en&filter[author][notNull]=true", access)).Items.ShouldBeEmpty();
        (await _database.RunAsync($"locale=en&filter[author][null]=true&filter[id][eq]={article}", access)).Items.Single()["title"].ShouldBe("With author");
    }

    [Fact]
    public async Task AFilterOnARelationDoesNotMatchATargetARowRuleDenies()
    {
        var ann = _database.AddAuthor("Ann");
        var article = _database.AddArticle("With author");
        _database.Relate("article", article, "author", "author", ann);

        var access = new QueryAccess();
        access.RowRules["author"] = new FieldFilter("name", FilterOperator.Equal, "Somebody else");

        (await _database.RunAsync($"locale=en&filter[author][eq]={ann}", access)).Items.ShouldBeEmpty();
        (await _database.RunAsync("locale=en&filter[author][notNull]=true", access)).Items.ShouldBeEmpty();
        (await _database.RunAsync($"locale=en&filter[author][null]=true&filter[id][eq]={article}", access)).Items.Single()["title"].ShouldBe("With author");

        access.RowRules["author"] = new FieldFilter("name", FilterOperator.Equal, "Ann");

        (await _database.RunAsync($"locale=en&filter[author][eq]={ann}", access)).Items.Single()["title"].ShouldBe("With author");
    }

    [Fact]
    public async Task ARowRuleThroughARelationGoesThroughATypeTheClientCannotRead()
    {
        var ann = _database.AddAuthor("Ann", secret: "mine");
        var article = _database.AddArticle("With author");
        _database.Relate("article", article, "author", "author", ann);
        _database.AddArticle("Without");

        var access = new QueryAccess();
        access.DeniedTypes.Add("author");
        access.RowRules["article"] = new FieldFilter("author.secret", FilterOperator.Equal, "mine");
        access.RowRules["author"] = new FieldFilter("name", FilterOperator.Equal, "Somebody else");

        (await _database.RunAsync("locale=en", access)).Items.Single()["title"].ShouldBe("With author");
    }

    [Fact]
    public void TheEngineIsInTheContainer() =>
        _database.Services.GetService<ContentQueryEngine>().ShouldNotBeNull();
}
