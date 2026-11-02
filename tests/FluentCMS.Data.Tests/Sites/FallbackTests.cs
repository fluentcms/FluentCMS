using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Localization;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Sites;

public sealed class FallbackTests : IDisposable
{
    private const string EnglishBody = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hi"}]}]}""";

    private static readonly SiteId Site = new(QueryDatabase.SiteA);

    private readonly QueryDatabase _database = new();
    private readonly Guid _hello;

    public FallbackTests()
    {
        var context = _database.Context;
        context.Add(new Site { Id = QueryDatabase.SiteA, Name = "A", Slug = "a", DefaultLocale = "en", CreatedAt = DateTimeOffset.UtcNow });
        context.AddRange(
            new SiteLocale { SiteId = QueryDatabase.SiteA, Code = "en", Position = 0 },
            new SiteLocale { SiteId = QueryDatabase.SiteA, Code = "fr", Position = 1 },
            new SiteLocale { SiteId = QueryDatabase.SiteA, Code = "fr-CA", Position = 2 });
        context.SaveChanges();

        _hello = _database.AddArticle("Hello", new() { ["subtitle"] = "Sub en", ["body"] = EnglishBody });
        _database.AddArticle("Bonjour", new() { ["subtitle"] = "Sous-titre" }, locale: "fr", id: _hello);
        _database.AddArticle("Salut", null, locale: "fr-CA", id: _hello);
    }

    public void Dispose() => _database.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LocaleFallbackReader Reader(IContentQueryAccess? access = null, ContentQueryOptions? options = null) =>
        new(_database.Engine(access, options), QueryDatabase.Schema, new LocaleRegistry(_database.Context), options ?? new ContentQueryOptions());

    private async Task<IReadOnlyList<ContentDocument>> ReadAsync(string query, IContentQueryAccess? access = null, ContentQueryOptions? options = null, LocaleFallbackReader? reader = null)
    {
        var parsed = BracketQueryParser.Parse(query) with { Site = Site };
        reader ??= Reader(access, options);
        var result = await _database.Engine(access, options).ExecuteAsync("article", parsed, Token);

        var chain = await reader.ChainAsync(Site, parsed.Locale!, parsed.Fallback, Token);
        await reader.ApplyAsync("article", parsed, result.Items, chain, Token);
        return result.Items;
    }

    private static ContentDocument FallbacksOf(ContentDocument entry) => entry["fallbacks"].ShouldBeOfType<ContentDocument>();

    [Fact]
    public async Task ASiteChainRunsFromFrenchCanadianThroughFrenchToEnglish()
    {
        var chain = await Reader().ChainAsync(Site, "fr-CA", LocaleFallback.Chain, Token);

        chain.Locales.ShouldBe(["fr-CA", "fr", "en"]);
        chain.Requested.ShouldBe("fr-CA");
    }

    [Fact]
    public async Task AMissingFieldFallsBackThroughTheChainAndNamesTheLocaleThatServedIt()
    {
        var entry = (await ReadAsync("locale=fr-CA")).Single();

        entry["locale"].ShouldBe("fr-CA");
        entry["title"].ShouldBe("Salut");
        entry["subtitle"].ShouldBe("Sous-titre");
        entry["body"].ShouldBe(new RichText(EnglishBody));

        var fallbacks = FallbacksOf(entry);
        fallbacks.Keys.ShouldBe(["subtitle", "body"]);
        fallbacks["subtitle"].ShouldBe("fr");
        fallbacks["body"].ShouldBe("en");
    }

    [Fact]
    public async Task AFieldWithAValueInTheRequestedLocaleIsNeverReplacedOrReported()
    {
        var entry = (await ReadAsync("locale=fr")).Single();

        entry["title"].ShouldBe("Bonjour");
        entry["subtitle"].ShouldBe("Sous-titre");
        FallbacksOf(entry).Keys.ShouldBe(["body"]);
    }

    [Fact]
    public async Task AFieldThatHasNoValueInAnyLocaleStaysEmptyAndIsNotReported()
    {
        var entry = (await ReadAsync("locale=fr-CA")).Single();

        entry["sponsor"].ShouldBeNull();
        FallbacksOf(entry).ContainsKey("sponsor").ShouldBeFalse();
    }

    [Fact]
    public async Task FallbackNoneReadsTheRequestedLocaleAlone()
    {
        var parsed = BracketQueryParser.Parse("locale=fr-CA&fallback=none");
        (await Reader().ChainAsync(Site, "fr-CA", parsed.Fallback, Token)).Locales.ShouldBe(["fr-CA"]);

        var entry = (await ReadAsync("locale=fr-CA&fallback=none")).Single();

        entry["subtitle"].ShouldBeNull();
        entry["body"].ShouldBe(RichText.Empty);
        entry.ContainsKey("fallbacks").ShouldBeFalse();
    }

    [Fact]
    public async Task AListFiltersOnTheRequestedLocaleOnlyAndFallbackDoesNotBringEntriesIn()
    {
        _database.AddArticle("Only English", new() { ["subtitle"] = "Never shown" });

        (await ReadAsync("locale=en")).Select(entry => entry["title"]).ShouldBe(["Hello", "Only English"], ignoreOrder: true);

        var inFrench = await ReadAsync("locale=fr-CA&sort=-createdAt");
        inFrench.Select(entry => entry["title"]).ShouldBe(["Salut"]);
    }

    [Fact]
    public async Task AFilterOnALocalizedFieldNeverLooksAtAFallbackLocale()
    {
        (await ReadAsync("locale=fr-CA&filter[subtitle][eq]=Sous-titre")).ShouldBeEmpty();
        (await ReadAsync("locale=fr&filter[subtitle][eq]=Sous-titre")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task AFieldTheQueryDidNotSelectIsNotFilled()
    {
        var entry = (await ReadAsync("locale=fr-CA&fields=title")).Single();

        entry.Keys.ShouldBe(["id", "title"]);
    }

    [Fact]
    public async Task ALocaleThePrincipalCannotReadIsNotAFallbackSource()
    {
        var entry = (await ReadAsync("locale=fr-CA", new FrenchIsClosed())).Single();

        entry["subtitle"].ShouldBe("Sub en");
        FallbacksOf(entry)["subtitle"].ShouldBe("en");
    }

    [Fact]
    public async Task AFullPageOfEntriesAllFallBack()
    {
        for (var index = 1; index < 100; index++)
        {
            var id = _database.AddArticle($"English {index}", new() { ["subtitle"] = $"Sub {index}" });
            _database.AddArticle($"Salut {index}", null, locale: "fr-CA", id: id);
        }

        var page = await ReadAsync("locale=fr-CA&pageSize=100");

        page.Count.ShouldBe(100);
        page.ShouldAllBe(entry => entry.ContainsKey("fallbacks") && entry["subtitle"] != null);
    }

    [Fact]
    public async Task ASmallFilterCapStillReadsEveryEntryOfThePage()
    {
        for (var index = 1; index < 12; index++)
        {
            var id = _database.AddArticle($"English {index}", new() { ["subtitle"] = $"Sub {index}" });
            _database.AddArticle($"Salut {index}", null, locale: "fr-CA", id: id);
        }

        var page = await ReadAsync("locale=fr-CA", options: new ContentQueryOptions { MaxFilterNodes = 4 });

        page.Count.ShouldBe(12);
        page.ShouldAllBe(entry => entry["subtitle"] != null);
    }

    [Fact]
    public async Task ALimitErrorInAFallbackReadReachesTheCaller()
    {
        var author = _database.AddAuthor("Ann");
        _database.Relate("article", _hello, "sponsor", "author", author, locale: "fr");
        var strict = new ContentQueryOptions { MaxPopulatedEntries = 0 };

        var error = await Should.ThrowAsync<ValidationException>(() => ReadAsync("locale=fr-CA&populate=sponsor", reader: Reader(options: strict)));

        error.Errors.ShouldContain(item => item.Code == "FCMS2207");
    }

    [Fact]
    public async Task LocaleAllReadsEveryLocaleOfAnEntryAsAMapInTheOrderTheSiteSetsThem()
    {
        var parsed = BracketQueryParser.Parse("locale=en") with { Site = Site };

        var all = await Reader().ReadAllAsync("article", parsed, [_hello], Site, Token);

        var map = all[_hello];
        map.Keys.ShouldBe(["en", "fr", "fr-CA"]);
        map.Keys.Select(locale => ((ContentDocument)map[locale]!)["title"]).ShouldBe(["Hello", "Bonjour", "Salut"]);
        ((ContentDocument)map["fr-CA"]!)["subtitle"].ShouldBeNull();
    }

    [Fact]
    public async Task LocaleAllSkipsALocaleInWhichTheEntryHasNoRow()
    {
        var onlyEnglish = _database.AddArticle("Only English");
        var parsed = BracketQueryParser.Parse("locale=en") with { Site = Site };

        var all = await Reader().ReadAllAsync("article", parsed, [onlyEnglish], Site, Token);

        all[onlyEnglish].Keys.ShouldBe(["en"]);
    }

    [Fact]
    public async Task AListStillRejectsLocaleAllBecauseItNeedsOneLocale()
    {
        await Should.ThrowAsync<ValidationException>(() => _database.RunAsync("locale=all"));
    }

    // A principal whose grants cover every locale but French.
    private sealed class FrenchIsClosed : IContentQueryAccess
    {
        private readonly OpenContentQueryAccess _open = new();

        public ValueTask<IContentQueryAccess> ForQueryAsync(SiteId? site, string locale, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IContentQueryAccess>(locale == "fr" ? new QueryAccess { DeniedTypes = { "article" } } : this);

        public bool CanReadType(SchemaType type) => _open.CanReadType(type);

        public bool CanReadDraft(SchemaType type) => _open.CanReadDraft(type);

        public bool CanReadField(SchemaType type, string field) => _open.CanReadField(type, field);

        public ContentFilter? RowRule(SchemaType type) => _open.RowRule(type);
    }
}
