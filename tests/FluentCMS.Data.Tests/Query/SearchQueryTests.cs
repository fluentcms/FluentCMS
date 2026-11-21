using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Query;

public sealed class SearchQueryTests : IDisposable
{
    private readonly QueryDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static List<Guid> Ids(ContentQueryResult result) => [.. result.Items.Select(item => (Guid)item["id"]!)];

    [Fact]
    public async Task TheListIsNarrowedToTheIdsTheIndexFindsInTheOrderOfTheQuery()
    {
        var first = _database.AddArticle("First");
        _database.AddArticle("Second");
        var third = _database.AddArticle("Third");
        var index = new FakeSearchIndex(third, first);

        var result = await _database.RunAsync("locale=en&q=hello&sort=title&withTotal=true", search: index);

        Ids(result).ShouldBe([first, third]);
        result.Total.ShouldBe(2);
        index.Requests.ShouldHaveSingleItem().ShouldBe(new SearchRequest("article", "hello", 1000) { Locale = "en", Status = QueryStatus.Published });
    }

    [Fact]
    public async Task TheFilterRowRuleSiteAndStatusStillApplyToWhatTheIndexFinds()
    {
        var kept = _database.AddArticle("Kept", new() { ["views"] = 1L });
        var filtered = _database.AddArticle("Filtered", new() { ["views"] = 9L });
        var ruled = _database.AddArticle("Ruled", new() { ["views"] = 2L });
        var draft = _database.AddArticle("Draft", new() { ["views"] = 1L }, state: ContentState.Draft);
        var elsewhere = _database.AddArticle("Elsewhere", new() { ["views"] = 1L }, site: QueryDatabase.SiteB);
        var access = new QueryAccess { RowRules = { ["article"] = new FieldFilter("views", FilterOperator.NotEqual, 2L) } };
        var index = new FakeSearchIndex(kept, filtered, ruled, draft, elsewhere);

        var result = await _database.RunAsync($"locale=en&site={QueryDatabase.SiteA}&q=x&filter[views][lt]=5", access, search: index);

        Ids(result).ShouldBe([kept]);
        index.Requests.ShouldHaveSingleItem().Site.ShouldBe(new SiteId(QueryDatabase.SiteA));
    }

    // A match on a field the caller may not read would tell what it holds, so the index is asked for the readable ones,
    // and with none of them nothing matches, though the index is still asked so that it can refuse.
    [Fact]
    public async Task TheIndexIsAskedForTheSearchableFieldsTheCallerMayReadOnly()
    {
        var article = _database.AddArticle("First");
        var index = new FakeSearchIndex(article);

        await _database.RunAsync("locale=en&q=x", new QueryAccess(), search: index);
        await _database.RunAsync("locale=en&q=x", new QueryAccess { DeniedFields = { "article.subtitle", "article.body" } }, search: index);
        var none = await _database.RunAsync("locale=en&q=x&withTotal=true", new QueryAccess { DeniedFields = { "article.title", "article.subtitle" } }, search: index);

        index.Requests.Select(request => request.Fields).ShouldBe([null, ["title"], []]);
        none.Items.ShouldBeEmpty();
        none.Total.ShouldBe(0);
    }

    [Fact]
    public async Task AnIndexThatCannotMatchByFieldIsRefusedToAFieldLimitedCallerOnly()
    {
        var article = _database.AddArticle("First");
        var index = new FakeSearchIndex(article) { ByField = false };

        Ids(await _database.RunAsync("locale=en&q=x", new QueryAccess(), search: index)).ShouldBe([article]);
        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&q=x", new QueryAccess { DeniedFields = { "article.subtitle" } }, search: index));

        var error = exception.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(QueryErrorCodes.NoSearchIndex);
        error.Path.ShouldBe("q");
        index.Requests.ShouldHaveSingleItem().Fields.ShouldBeNull();
    }

    [Fact]
    public async Task TheIndexIsAskedForTheStateTheQueryReads()
    {
        var draft = _database.AddArticle("Draft", state: ContentState.Draft);
        var index = new FakeSearchIndex(draft);

        var result = await _database.RunAsync("locale=en&status=draft&q=x", new QueryAccess(), search: index);

        Ids(result).ShouldBe([draft]);
        index.Requests.ShouldHaveSingleItem().Status.ShouldBe(QueryStatus.Draft);
    }

    [Fact]
    public async Task ATypeThatIsNotLocalizedOrNotOfASiteIsSearchedWithoutALocaleOrSite()
    {
        var author = _database.AddAuthor("Ann");
        var tag = _database.AddTag("net");
        var index = new FakeSearchIndex(author, tag);

        Ids(await _database.RunAsync($"locale=en&site={QueryDatabase.SiteA}&q=x", type: "author", search: index)).ShouldBe([author]);
        Ids(await _database.RunAsync($"locale=en&site={QueryDatabase.SiteA}&q=x", type: "tag", search: index)).ShouldBe([tag]);

        index.Requests.Select(request => (request.Type, request.Locale, request.Site)).ShouldBe(
        [
            ("author", "", new SiteId(QueryDatabase.SiteA)),
            ("tag", "", (SiteId?)null),
        ]);
    }

    [Fact]
    public async Task TheEngineReadsNoMoreIdsThanItAskedFor()
    {
        var first = _database.AddArticle("First");
        var second = _database.AddArticle("Second");
        var index = new FakeSearchIndex(second, first);

        var result = await _database.RunAsync("locale=en&q=x", options: new ContentQueryOptions { MaxSearchResults = 1 }, search: index);

        Ids(result).ShouldBe([second]);
        index.Requests.ShouldHaveSingleItem().Limit.ShouldBe(1);
    }

    // As many ids as asked for may leave matches out, which the list says; fewer, or no search, say nothing.
    [Fact]
    public async Task AListSaysItsSearchIsCutWhenTheIndexReturnsTheMostIdsItWasAskedFor()
    {
        var first = _database.AddArticle("First");
        var second = _database.AddArticle("Second");
        var options = new ContentQueryOptions { MaxSearchResults = 2 };

        var cut = await _database.RunAsync("locale=en&q=x&filter[title][eq]=First&withTotal=true", options: options, search: new FakeSearchIndex(second, first, Guid.CreateVersion7()));
        var whole = await _database.RunAsync("locale=en&q=x", options: options, search: new FakeSearchIndex(first));
        var unsearched = await _database.RunAsync("locale=en", options: options);

        Ids(cut).ShouldBe([first]);
        cut.Total.ShouldBe(1);
        cut.SearchTruncated.ShouldBeTrue();
        whole.SearchTruncated.ShouldBeFalse();
        unsearched.SearchTruncated.ShouldBeFalse();
    }

    // What the index found in fields the caller may not read must not show, not even as a cut.
    [Fact]
    public async Task ACallerWhoMayReadNoSearchableFieldIsNeverToldOfACut()
    {
        var first = _database.AddArticle("First");
        var index = new FakeSearchIndex(first, Guid.CreateVersion7());

        var result = await _database.RunAsync("locale=en&q=x", new QueryAccess { DeniedFields = { "article.title", "article.subtitle" } }, new ContentQueryOptions { MaxSearchResults = 2 }, search: index);

        result.Items.ShouldBeEmpty();
        result.SearchTruncated.ShouldBeFalse();
    }

    [Fact]
    public async Task ACountIsNarrowedToo()
    {
        var first = _database.AddArticle("First");
        _database.AddArticle("Second");
        var query = new ContentQuery { Locale = "en", Search = "x" };

        var count = await _database.Engine(search: new FakeSearchIndex(first)).CountAsync("article", query, TestContext.Current.CancellationToken);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task WithNoIndexASearchIsRefused()
    {
        _database.AddArticle("First");

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=en&q=hello"));

        var error = exception.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(QueryErrorCodes.NoSearchIndex);
        error.Path.ShouldBe("q");
        error.Message.ShouldContain("ISearchIndex");
    }

    [Theory]
    [InlineData("q=", QueryErrorCodes.InvalidValue)]
    [InlineData("q=+++", QueryErrorCodes.InvalidValue)]
    [InlineData("q=x&filter[nothing][eq]=1", QueryErrorCodes.UnknownField)]
    public async Task ARefusedQueryNeverAsksTheIndex(string queryString, string code)
    {
        var index = new FakeSearchIndex();

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync($"locale=en&{queryString}", search: index));

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
        index.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASearchTextHasAtMost256Characters()
    {
        var index = new FakeSearchIndex();
        await _database.RunAsync($"locale=en&q={new string('a', 256)}", search: index);

        var exception = await Should.ThrowAsync<ValidationException>(() => _database.RunAsync($"locale=en&q={new string('a', 257)}", search: index));

        exception.Errors.ShouldHaveSingleItem().Code.ShouldBe(QueryErrorCodes.LimitExceeded);
        index.Requests.Count.ShouldBe(1);
    }

    private sealed class FakeSearchIndex(params Guid[] ids) : ISearchIndex
    {
        public List<SearchRequest> Requests { get; } = [];

        public bool ByField { get; init; } = true;

        public bool MatchesByField => ByField;

        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult<IReadOnlyList<Guid>>(ids);
        }
    }
}
