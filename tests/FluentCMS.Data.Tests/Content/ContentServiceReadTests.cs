using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Authorization;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

public sealed class ContentServiceReadTests : IDisposable
{
    private const string Body = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]}""";

    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static ContentDocument Titled(string title) => new() { ["title"] = title };

    private static GetOptions Draft(string? locale = null, LocaleFallback fallback = LocaleFallback.Chain) => new() { Status = QueryStatus.Draft, Locale = locale, Fallback = fallback };

    [Fact]
    public async Task AQueryMapsToAPagedResult()
    {
        for (var index = 0; index < 3; index++)
        {
            await _database.CreateAsync(Editor, Titled($"Entry {index}"));
        }

        var page = await _database.QueryAsync(Editor, new ContentQuery
        {
            Status = QueryStatus.Draft,
            Page = new PageRequest { Number = 1, Size = 2 },
            Sort = [new SortField("createdAt")],
        });

        page.Items.Count.ShouldBe(2);
        page.Total.ShouldBe(3);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(2);
        page.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public async Task AReadWithoutASiteOrLocaleStaysInTheOperation()
    {
        var query = new ContentQuery { Status = QueryStatus.Draft };
        query.Site.ShouldBeNull();
        query.Locale.ShouldBeNull();

        await _database.CreateAsync(Editor, Titled("In A"), site: SiteA);
        await _database.CreateAsync(Editor, Titled("In B"), site: SiteB);
        await _database.CreateAsync(Editor, Titled("In French"), locale: "fr", site: SiteA);

        (await _database.QueryAsync(Editor, query, locale: "en", site: SiteA)).Items.ShouldHaveSingleItem()["title"].ShouldBe("In A");
        (await _database.QueryAsync(Editor, query, locale: "en", site: SiteB)).Items.ShouldHaveSingleItem()["title"].ShouldBe("In B");
        var french = (await _database.QueryAsync(Editor, query, locale: "fr", site: SiteA)).Items.ShouldHaveSingleItem();
        french["title"].ShouldBe("In French");
        french["locale"].ShouldBe("fr");

        (await _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CountAsync("article", new CountOptions { Status = QueryStatus.Draft }, Token), locale: "en", site: SiteB)).ShouldBe(1);
    }

    [Fact]
    public async Task ADraftIsNotPublished()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Draft"));

        (await _database.GetAsync(Editor, IdOf(entry), new GetOptions())).ShouldBeNull();
        (await _database.QueryAsync(Editor, new ContentQuery())).Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("xx-")]
    [InlineData("de")]
    public async Task AnInvalidOrUnlistedLocaleIsRefusedOnReadAndWrite(string locale)
    {
        await Should.ThrowAsync<ValidationException>(() => _database.GetAsync(Editor, Guid.CreateVersion7(), locale: locale));
        await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Titled("x"), locale: locale));
    }

    [Fact]
    public async Task AWriteNeedsOneLocale()
    {
        (await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, Titled("x"), locale: "all")))
            .Errors.ShouldHaveSingleItem().Path.ShouldBe("locale");
    }

    [Fact]
    public async Task AReadFallsBackThroughTheChain()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub en", ["body"] = Body });
        var id = IdOf(entry);
        await _database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["subtitle"] = "Sous-titre" }, 0, locale: "fr");
        await _database.UpdateAsync(Editor, id, Titled("Salut"), 0, locale: "fr-CA");

        var read = (await _database.GetAsync(Editor, id, Draft("fr-CA")))!;

        read["title"].ShouldBe("Salut");
        read["subtitle"].ShouldBe("Sous-titre");
        ((RichText)read["body"]!).Json.ShouldBe(Body);
        var fallbacks = (ContentDocument)read[LocaleFallbackReader.FallbacksKey]!;
        fallbacks["subtitle"].ShouldBe("fr");
        fallbacks["body"].ShouldBe("en");

        var none = (await _database.GetAsync(Editor, id, Draft("fr-CA", LocaleFallback.None)))!;
        none["subtitle"].ShouldBeNull();
        none.ContainsKey(LocaleFallbackReader.FallbacksKey).ShouldBeFalse();

        var listed = (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Locale = "fr-CA" })).Items.ShouldHaveSingleItem();
        listed["subtitle"].ShouldBe("Sous-titre");
    }

    [Fact]
    public async Task AnEntryByIdWithNoRowInTheLocaleIsServedFromTheChainAndAListLeavesItOut()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub" });

        var read = (await _database.GetAsync(Editor, IdOf(entry), Draft("fr-CA")))!;

        read["title"].ShouldBe("Hello");
        read["locale"].ShouldBe("fr-CA");
        var fallbacks = (ContentDocument)read[LocaleFallbackReader.FallbacksKey]!;
        fallbacks["title"].ShouldBe("en");
        fallbacks["subtitle"].ShouldBe("en");

        (await _database.GetAsync(Editor, IdOf(entry), Draft("fr-CA", LocaleFallback.None))).ShouldBeNull();
        (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Locale = "fr-CA" })).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEntryServedFromTheChainIsLimitedByTheRequestedLocaleToo()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub" });
        var role = _database.Database.AddRoleForTest(
            "PerLocale",
            ("content.article.read", new PermissionConstraints { Locales = ["en"] }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr-CA"], Fields = ["title"] }));

        var read = (await _database.GetAsync(WithRole(role), IdOf(entry), Draft("fr-CA")))!;

        read["title"].ShouldBe("Hello");
        read.ContainsKey("subtitle").ShouldBeFalse();
        ((ContentDocument)read[LocaleFallbackReader.FallbacksKey]!).Keys.ShouldBe(["title"]);
    }

    [Fact]
    public async Task EveryLocaleComesBackAsAMap()
    {
        var entry = await _database.CreateAsync(Editor, Titled("Hello"));
        var id = IdOf(entry);
        await _database.UpdateAsync(Editor, id, Titled("Bonjour"), 0, locale: "fr");

        var read = (await _database.GetAsync(Editor, id, Draft("all")))!;
        read["id"].ShouldBe(id);
        var map = (ContentDocument)read["locales"]!;
        map.Keys.ShouldBe(["en", "fr"]);
        ((ContentDocument)map["fr"]!)["title"].ShouldBe("Bonjour");

        var listed = (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Locale = "all" })).Items.ShouldHaveSingleItem();
        ((ContentDocument)listed["locales"]!).Keys.ShouldBe(["en", "fr"]);

        (await _database.GetAsync(Editor, Guid.CreateVersion7(), Draft("all"))).ShouldBeNull();
    }

    // #3277: each entry once, one without a default-locale row too, matched, sorted, paged and counted by its row in the
    // first locale of the site that has one.
    [Fact]
    public async Task AListOfEveryLocaleHoldsEachEntryOnceWhateverLocalesItHas()
    {
        var english = IdOf(await _database.CreateAsync(Editor, Titled("B english")));
        await _database.UpdateAsync(Editor, english, Titled("A anglais"), 0, locale: "fr");
        var french = IdOf(await _database.CreateAsync(Editor, Titled("C french"), locale: "fr"));
        var canadian = IdOf(await _database.CreateAsync(Editor, Titled("A canadian"), locale: "fr-CA"));
        await _database.UpdateAsync(Editor, canadian, Titled("D quebec"), 0, locale: "fr");

        var all = new ContentQuery { Status = QueryStatus.Draft, Locale = "all", Sort = [new SortField("title")], Page = new PageRequest { Size = 2 } };
        var first = await _database.QueryAsync(Editor, all);
        first.Total.ShouldBe(3);
        first.Items.Select(item => item["id"]).ShouldBe([english, french]);
        ((ContentDocument)first.Items[1]["locales"]!).Keys.ShouldBe(["fr"]);

        var next = await _database.QueryAsync(Editor, all with { Page = new PageRequest { Size = 2, WithTotal = false }, Cursor = first.NextCursor });
        var last = next.Items.ShouldHaveSingleItem();
        last["id"].ShouldBe(canadian);
        ((ContentDocument)last["locales"]!).Keys.ShouldBe(["fr", "fr-CA"]);
        next.NextCursor.ShouldBeNull();

        (await _database.QueryAsync(Editor, all with { Page = new PageRequest { Number = 2, Size = 2 } })).Items.ShouldHaveSingleItem()["id"].ShouldBe(canadian);

        // The filter asks the row the entry is matched by, which for an entry with an English row is that one.
        (await _database.QueryAsync(Editor, all with { Filter = new FieldFilter("title", FilterOperator.Equal, "C french") })).Items.ShouldHaveSingleItem()["id"].ShouldBe(french);
        (await _database.QueryAsync(Editor, all with { Filter = new FieldFilter("title", FilterOperator.Equal, "A anglais") })).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AListOfEveryLocaleShowsNothingOfALocaleThePrincipalMayNotRead()
    {
        var english = IdOf(await _database.CreateAsync(Editor, Titled("Hello")));
        await _database.UpdateAsync(Editor, english, Titled("Bonjour"), 0, locale: "fr");
        var canadian = IdOf(await _database.CreateAsync(Editor, Titled("Allo"), locale: "fr-CA"));
        await _database.CreateAsync(Editor, Titled("Only English"));
        var role = _database.Database.AddRoleForTest("French", ("content.article.read", new PermissionConstraints { Locales = ["fr", "fr-CA"] }));

        var page = await _database.QueryAsync(WithRole(role), new ContentQuery { Status = QueryStatus.Draft, Locale = "all", Sort = [new SortField("title")] });

        page.Total.ShouldBe(2);
        page.Items.Select(item => item["id"]).ShouldBe([canadian, english]);
        page.Items.Select(item => ((ContentDocument)item["locales"]!).Keys.ShouldHaveSingleItem()).ShouldBe(["fr-CA", "fr"]);
    }

    // An English row the principal's rule hides does not anchor the entry: its French row, which it may read, does.
    [Fact]
    public async Task AListOfEveryLocaleAnchorsAnEntryInTheFirstLocaleWhoseRuleLetsItThrough()
    {
        using var database = new ServiceDatabase(cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));
        var abroad = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Abroad", ["region"] = "us" }));
        await database.UpdateAsync(Editor, abroad, Titled("A l'etranger"), 0, locale: "fr");
        var local = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Local", ["region"] = "eu" }));
        await database.CreateAsync(Editor, new() { ["title"] = "Elsewhere", ["region"] = "us" });
        var role = database.Database.AddRoleForTest(
            "Regional",
            ("content.article.read", new PermissionConstraints { Locales = ["en"], RowRule = "region" }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr"] }));
        var regional = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });

        var page = await database.QueryAsync(regional, new ContentQuery { Status = QueryStatus.Draft, Locale = "all", Sort = [new SortField("title")] });

        page.Total.ShouldBe(2);
        page.Items.Select(item => item["id"]).ShouldBe([abroad, local]);
        ((ContentDocument)page.Items[0]["locales"]!).Keys.ShouldBe(["fr"]);
        ((ContentDocument)page.Items[1]["locales"]!).Keys.ShouldBe(["en"]);
    }

    // Finds the ids in every locale, or in Locale alone when a test sets it, as text that holds the word there only.
    private sealed class Hits
    {
        public List<Guid> Ids { get; } = [];

        public string? Locale { get; set; }

        public bool MatchesByField { get; init; } = true;

        public List<SearchRequest> Requests { get; } = [];
    }

    private sealed class FakeSearch(Hits hits) : ISearchIndex
    {
        public bool MatchesByField => hits.MatchesByField;

        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            hits.Requests.Add(request);
            return Task.FromResult<IReadOnlyList<Guid>>(hits.Locale is null || hits.Locale == request.Locale ? hits.Ids : []);
        }
    }

    private static ServiceDatabase Searching(Hits hits, Action<FluentCmsBuilder>? configure = null, SchemaModel? schema = null) => new(
        cms =>
        {
            cms.Services.AddSingleton(hits);
            cms.Providers.Use<ISearchIndex, FakeSearch>();
            configure?.Invoke(cms);
        },
        schema);

    // #3469: the search chose the entries in French; what English fills in for them does not have to match.
    [Fact]
    public async Task AFallbackReadOfASearchedListFillsFieldsFromALocaleWhoseTextDoesNotMatch()
    {
        var hits = new Hits { Locale = "fr" };
        using var database = Searching(hits);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub en" }));
        await database.UpdateAsync(Editor, id, Titled("Fusion"), 0, locale: "fr");
        hits.Ids.Add(id);

        var listed = (await database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Locale = "fr", Fallback = LocaleFallback.Chain, Search = "fusion" })).Items.ShouldHaveSingleItem();

        listed["title"].ShouldBe("Fusion");
        listed["subtitle"].ShouldBe("Sub en");
        ((ContentDocument)listed[LocaleFallbackReader.FallbacksKey]!)["subtitle"].ShouldBe("en");
        hits.Requests.ShouldHaveSingleItem().Locale.ShouldBe("fr");
    }

    // #3469: the search chose the entries in English, the first locale; every locale of each is in its map.
    [Fact]
    public async Task ALocaleAllReadOfASearchedListHoldsEveryLocaleOfEachEntry()
    {
        var hits = new Hits { Locale = "en" };
        using var database = Searching(hits);
        var id = IdOf(await database.CreateAsync(Editor, Titled("Merger")));
        await database.UpdateAsync(Editor, id, Titled("Fusion"), 0, locale: "fr");
        hits.Ids.Add(id);

        var listed = (await database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Locale = "all", Search = "merger" })).Items.ShouldHaveSingleItem();

        var map = (ContentDocument)listed["locales"]!;
        map.Keys.ShouldBe(["en", "fr"]);
        ((ContentDocument)map["fr"]!)["title"].ShouldBe("Fusion");
        hits.Requests.ShouldHaveSingleItem().Locale.ShouldBe("en");
    }

    // The reads of the other locales name the ids, one at a time under this cap, and ask the index nothing: the order, the
    // total, the cursor and searchTruncated are those of the one search, as they are without a fallback. Three hits under a
    // limit of three are cut.
    [Theory]
    [InlineData(3, true)]
    [InlineData(1000, false)]
    public async Task ASearchedListAsksTheIndexOnceWhateverLocalesAndBatchesItReads(int maxSearchResults, bool truncated)
    {
        var hits = new Hits();
        using var database = Searching(hits, cms => cms.Services.AddSingleton(new ContentQueryOptions { MaxFilterNodes = 2, MaxSearchResults = maxSearchResults }));
        foreach (var title in new[] { "C", "A", "B" })
        {
            var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = $"{title} english", ["subtitle"] = $"Sub {title}" }));
            await database.UpdateAsync(Editor, id, Titled($"{title} quebec"), 0, locale: "fr-CA");
            hits.Ids.Add(id);
        }

        var searched = new ContentQuery { Status = QueryStatus.Draft, Search = "word", Sort = [new SortField("title")], Page = new PageRequest { Size = 2 } };
        foreach (var query in new[] { searched with { Locale = "fr-CA" }, searched with { Locale = "all" } })
        {
            hits.Requests.Clear();
            var page = await database.QueryAsync(Editor, query with { Fallback = LocaleFallback.Chain });
            hits.Requests.Count.ShouldBe(1);

            var alone = await database.QueryAsync(Editor, query with { Fallback = LocaleFallback.None });
            page.Items.Select(item => item["id"]).ShouldBe(alone.Items.Select(item => item["id"]));
            page.Items.Count.ShouldBe(2);
            page.Total.ShouldBe(3);
            page.NextCursor.ShouldNotBeNull();
            page.NextCursor.ShouldBe(alone.NextCursor);
            page.SearchTruncated.ShouldBe(truncated);
            alone.SearchTruncated.ShouldBe(truncated);
        }

        var french = (await database.QueryAsync(Editor, searched with { Locale = "fr-CA" })).Items;
        french.Select(item => item["subtitle"]).ShouldBe(["Sub A", "Sub B"]);
        var all = (await database.QueryAsync(Editor, searched with { Locale = "all" })).Items;
        all.Select(item => string.Join(',', ((ContentDocument)item["locales"]!).Keys)).ShouldBe(["en,fr-CA", "en,fr-CA"]);
    }

    // The index finds every entry in every locale here, so what the other locales hold is what the caller may read there:
    // the row rule of French and the state read. The first read leaves out the entries of another site and in the trash;
    // a locale's row has the site of its entry and goes to the trash with it, so no other-locale read meets one.
    [Fact]
    public async Task TheOtherLocalesOfASearchedListShowWhatTheCallerMayReadThereAlone()
    {
        var hits = new Hits();
        using var database = Searching(hits, cms => cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));
        async Task<Guid> ArticleAsync(string region, SiteId? site = null)
        {
            var title = $"{region} {hits.Ids.Count}";
            var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = $"{title} english", ["subtitle"] = $"Sub {region}", ["region"] = region }, site: site));
            await database.UpdateAsync(Editor, id, new() { ["title"] = $"{title} francais", ["subtitle"] = $"Sous-titre {region}", ["region"] = region }, 0, locale: "fr", site: site);
            await database.UpdateAsync(Editor, id, new() { ["title"] = $"{title} quebec", ["region"] = region }, 0, locale: "fr-CA", site: site);
            hits.Ids.Add(id);
            return id;
        }

        var abroad = await ArticleAsync("us");
        var local = await ArticleAsync("eu");
        await ArticleAsync("eu", SiteB);
        await database.DeleteAsync(Editor, await ArticleAsync("eu"));
        var role = database.Database.AddRoleForTest(
            "Regional",
            ("content.article.read", new PermissionConstraints { Locales = ["en", "fr-CA"] }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr"], RowRule = "region" }));
        var regional = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });
        var searched = new ContentQuery { Status = QueryStatus.Draft, Search = "word", Sort = [new SortField("createdAt")] };

        var french = (await database.QueryAsync(regional, searched with { Locale = "fr-CA", Fallback = LocaleFallback.Chain })).Items;
        french.Select(item => item["id"]).ShouldBe([abroad, local]);
        french.Select(item => item["subtitle"]).ShouldBe(["Sub us", "Sous-titre eu"]);

        var all = (await database.QueryAsync(regional, searched with { Locale = "all" })).Items;
        all.Select(item => item["id"]).ShouldBe([abroad, local]);
        all.Select(item => string.Join(',', ((ContentDocument)item["locales"]!).Keys)).ShouldBe(["en,fr-CA", "en,fr,fr-CA"]);

        // Published: the French draft of the subtitle is not served, and French has no row to list.
        await database.PublishAsync(Editor, local, locale: "en");
        await database.PublishAsync(Editor, local, locale: "fr-CA");
        var published = new ContentQuery { Search = "word" };

        (await database.QueryAsync(regional, published with { Locale = "fr-CA", Fallback = LocaleFallback.Chain })).Items.ShouldHaveSingleItem()["subtitle"].ShouldBe("Sub eu");
        ((ContentDocument)(await database.QueryAsync(regional, published with { Locale = "all" })).Items.ShouldHaveSingleItem()["locales"]!).Keys.ShouldBe(["en", "fr-CA"]);
    }

    // An index that cannot match by field refuses a caller limited in the searchable fields of the locale searched; a
    // limit in another locale of a list of every locale does not, since the read of that locale searches nothing.
    [Fact]
    public async Task AFieldLimitInAnotherLocaleOfASearchedListOfEveryLocaleDoesNotRefuseItsSearch()
    {
        var hits = new Hits { MatchesByField = false };
        using var database = Searching(hits, schema: QueryDatabase.Schema);
        var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["subtitle"] = "Sub en" }));
        await database.UpdateAsync(Editor, id, new() { ["title"] = "Fusion", ["subtitle"] = "Sous-titre" }, 0, locale: "fr");
        hits.Ids.Add(id);
        var role = database.Database.AddRoleForTest(
            "Translator",
            ("content.article.read", new PermissionConstraints { Locales = ["en"] }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr"], Fields = ["title"] }));
        var searched = new ContentQuery { Status = QueryStatus.Draft, Search = "word" };

        var map = (ContentDocument)(await database.QueryAsync(WithRole(role), searched with { Locale = "all" })).Items.ShouldHaveSingleItem()["locales"]!;
        map.Keys.ShouldBe(["en", "fr"]);
        ((ContentDocument)map["fr"]!).ContainsKey("subtitle").ShouldBeFalse();

        var refused = await Should.ThrowAsync<ValidationException>(() => database.QueryAsync(WithRole(role), searched with { Locale = "fr" }));
        (refused.Errors.ShouldHaveSingleItem().Path, refused.Errors.Single().Code).ShouldBe(("q", QueryErrorCodes.NoSearchIndex));
    }

    // The index knows no row rule. A hit on an English row the rule hides finds nothing, though the entry's French row could
    // be read: with a search, an entry's anchor is its row in the locale searched, under that locale's rule, alone.
    [Fact]
    public async Task AListOfEveryLocaleWithASearchAnchorsInTheSearchedLocaleAlone()
    {
        var hits = new Hits();
        using var database = new ServiceDatabase(cms =>
        {
            cms.Authorization.AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region"));
            cms.Services.AddSingleton(hits);
            cms.Providers.Use<ISearchIndex, FakeSearch>();
        });
        var abroad = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Merger abroad", ["region"] = "us" }));
        await database.UpdateAsync(Editor, abroad, Titled("Fusion"), 0, locale: "fr");
        var local = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Merger here", ["region"] = "eu" }));
        hits.Ids.AddRange([abroad, local]);
        var role = database.Database.AddRoleForTest(
            "Regional",
            ("content.article.read", new PermissionConstraints { Locales = ["en"], RowRule = "region" }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr"] }));
        var regional = AuthorizationDatabase.User([role], [TokenScopes.Management], claims: new() { ["region"] = "eu" });

        var page = await database.QueryAsync(regional, new ContentQuery { Status = QueryStatus.Draft, Locale = "all", Search = "merger" });

        page.Total.ShouldBe(1);
        page.Items.ShouldHaveSingleItem()["id"].ShouldBe(local);
        hits.Requests[0].Locale.ShouldBe("en");
    }

    // A filter or a sort of a list of every locale is asked of rows in each locale it reads, so a field the caller may read in
    // one of them and not another is refused, naming that locale.
    [Fact]
    public async Task AListOfEveryLocaleRefusesAFieldTheCallerMayNotReadInOneOfItsLocales()
    {
        await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 1 });
        var role = _database.Database.AddRoleForTest(
            "Translator",
            ("content.article.read", new PermissionConstraints { Locales = ["en"] }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr"], Fields = ["title"] }));
        var all = new ContentQuery { Status = QueryStatus.Draft, Locale = "all" };

        var filtered = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(WithRole(role), all with { Filter = new FieldFilter("views", FilterOperator.Equal, 1) }));
        var sorted = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(WithRole(role), all with { Sort = [new SortField("views")] }));

        var filter = filtered.Errors.ShouldHaveSingleItem();
        (filter.Path, filter.Code).ShouldBe(("views", QueryErrorCodes.UnknownField));
        filter.Message.ShouldContain("'fr'");
        var sort = sorted.Errors.ShouldHaveSingleItem();
        (sort.Path, sort.Code).ShouldBe(("sort", QueryErrorCodes.NotSortable));
        sort.Message.ShouldContain("'fr'");
        (await _database.QueryAsync(WithRole(role), all with { Filter = new FieldFilter("title", FilterOperator.Equal, "Hello"), Sort = [new SortField("title")] })).Total.ShouldBe(1);
    }

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }
    }

    // #3284: the populate cap holds for the whole answer, not for each locale it reads.
    [Fact]
    public async Task ThePopulateCapHoldsAcrossTheLocalesOfAListOfEveryLocale()
    {
        await AssertCapAsync(async database =>
        {
            var id = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Hello", ["sponsor"] = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author")) }));
            await database.UpdateAsync(Editor, id, new() { ["title"] = "Bonjour", ["sponsor"] = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Bea" }, "author")) }, 0, locale: "fr");
            return new ContentQuery { Status = QueryStatus.Draft, Locale = "all", Populate = ["sponsor"] };
        });
    }

    [Fact]
    public async Task ThePopulateCapHoldsAcrossTheLocalesAListFallsBackTo()
    {
        await AssertCapAsync(async database =>
        {
            var sponsor = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
            await database.CreateAsync(Editor, new() { ["title"] = "Own", ["sponsor"] = sponsor }, locale: "fr-CA");
            var fallsBack = IdOf(await database.CreateAsync(Editor, new() { ["title"] = "Bonjour", ["sponsor"] = sponsor }, locale: "fr"));
            await database.UpdateAsync(Editor, fallsBack, Titled("Allo"), 0, locale: "fr-CA");
            return new ContentQuery { Status = QueryStatus.Draft, Locale = "fr-CA", Fallback = LocaleFallback.Chain, Populate = ["sponsor"] };
        });
    }

    // Two related entries in all: refused under a cap of one, which each locale's read alone stays within.
    private static async Task AssertCapAsync(Func<ServiceDatabase, Task<ContentQuery>> arrange)
    {
        using (var strict = new ServiceDatabase(cms => cms.Services.AddSingleton(new ContentQueryOptions { MaxPopulatedEntries = 1 })))
        {
            var query = await arrange(strict);
            var refused = await Should.ThrowAsync<ValidationException>(() => strict.QueryAsync(Editor, query));
            (refused.Errors.ShouldHaveSingleItem().Path, refused.Errors.Single().Code).ShouldBe(("populate", QueryErrorCodes.LimitExceeded));
        }

        using var enough = new ServiceDatabase(cms => cms.Services.AddSingleton(new ContentQueryOptions { MaxPopulatedEntries = 2 }));
        var page = await enough.QueryAsync(Editor, await arrange(enough));
        page.Items.Count.ShouldBe(page.Items[0].ContainsKey("locales") ? 1 : 2);
    }

    [Fact]
    public async Task EveryLocaleOfAnEntryOmitsWhatThePrincipalMayNotRead()
    {
        var entry = await _database.CreateAsync(Editor, new() { ["title"] = "Hello", ["views"] = 5 });
        var role = _database.Database.AddRoleForTest("Limited", ("content.article.read", new Core.Authorization.PermissionConstraints { Fields = ["title"] }));

        var read = (await _database.GetAsync(WithRole(role), IdOf(entry), new GetOptions { Locale = "all", Status = QueryStatus.Draft }))!;

        ((ContentDocument)((ContentDocument)read["locales"]!)["en"]!).ContainsKey("views").ShouldBeFalse();
    }

    [Fact]
    public async Task RichTextIsRenderedAtEveryLevel()
    {
        var entry = await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["body"] = Body,
            ["sections"] = new List<ContentDocument> { new() { ["type"] = "hero", ["note"] = Body } },
        });

        var read = (await _database.GetAsync(Editor, IdOf(entry), new GetOptions { Status = QueryStatus.Draft, RichTextFormat = RichTextFormats.Html }))!;

        read["body"].ShouldBe("<p>Hi</p>");
        ((System.Text.Json.Nodes.JsonArray)read["sections"]!)[0]!["note"]!.GetValue<string>().ShouldBe("<p>Hi</p>");

        (await _database.GetAsync(Editor, IdOf(entry), new GetOptions { Status = QueryStatus.Draft, RichTextFormat = "json" }))!["body"].ShouldBeOfType<RichText>();
        (await Should.ThrowAsync<ValidationException>(() => _database.GetAsync(Editor, IdOf(entry), new GetOptions { RichTextFormat = "pdf" })))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe(QueryErrorCodes.InvalidParameter);
    }

    [Fact]
    public async Task ABlockFilterFindsWhatTheServiceStored()
    {
        await _database.CreateAsync(Editor, new()
        {
            ["title"] = "Hello",
            ["sections"] = new List<ContentDocument> { new() { ["type"] = "hero", ["at"] = "2026-11-02T10:00:00+02:00", ["on"] = "2026-11-02" } },
        });

        foreach (var filter in new[]
        {
            new FieldFilter("sections.hero.at", FilterOperator.Equal, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero)),
            new FieldFilter("sections.hero.on", FilterOperator.Equal, new DateOnly(2026, 11, 2)),
        })
        {
            (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft, Filter = filter })).Items.Count.ShouldBe(1);
        }
    }

    [Fact]
    public async Task CountCountsWhatThePrincipalMayRead()
    {
        var ann = AuthorRole(Guid.CreateVersion7());
        await _database.CreateAsync(ann, new() { ["title"] = "Mine", ["views"] = 1 });
        await _database.CreateAsync(ann, new() { ["title"] = "Mine too", ["views"] = 2 });
        await _database.CreateAsync(Editor, new() { ["title"] = "Theirs", ["views"] = 1 });

        (await Count(Editor, null)).ShouldBe(3);
        (await Count(ann, null)).ShouldBe(2);
        (await Count(ann, new FieldFilter("views", FilterOperator.Equal, 1L))).ShouldBe(1);
        (await _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CountAsync("article", null, Token))).ShouldBe(0);

        Task<long> Count(Principal principal, ContentFilter? filter) =>
            _database.AsAsync(principal, services => services.GetRequiredService<IContentService>().CountAsync("article", new CountOptions { Filter = filter, Status = QueryStatus.Draft }, Token));
    }

    [Fact]
    public async Task CountSendsTheCountAloneWithNoPageAndNoReferenceRead()
    {
        var trips = new RoundTrips();
        using var database = new ServiceDatabase(cms => cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(trips)));
        var author = IdOf(await database.CreateAsync(Editor, new() { ["name"] = "Ann" }, "author"));
        await database.CreateAsync(Editor, new() { ["title"] = "One", ["author"] = author });
        await database.CreateAsync(Editor, new() { ["title"] = "Two", ["author"] = author });

        var count = await trips.CountAsync(() => database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CountAsync("article", new CountOptions { Status = QueryStatus.Draft }, Token)));

        count.ShouldBe(2);
        var reads = trips.Sent.Where(sql => sql.Contains(TestProvider.Quote("cms_document"), StringComparison.Ordinal) || sql.Contains(TestProvider.Quote("cms_reference"), StringComparison.Ordinal)).ToList();
        reads.ShouldHaveSingleItem().ShouldMatch(@"(?i)COUNT(_BIG)?\(");
    }

    [Fact]
    public async Task AnUnknownTypeIsRefusedEverywhere()
    {
        await Should.ThrowAsync<ValidationException>(() => _database.GetAsync(Editor, Guid.CreateVersion7(), type: "nothing"));
        await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, [], "nothing"));
    }
}
