using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Localization;
using FluentCMS.Schema;
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
        await _database.CreateAsync(Editor, Titled("In A"));
        await _database.CreateAsync(Editor, Titled("In B"), site: SiteB);

        (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft })).Items.ShouldHaveSingleItem()["title"].ShouldBe("In A");
        (await _database.QueryAsync(Editor, new ContentQuery { Status = QueryStatus.Draft }, site: SiteB)).Items.ShouldHaveSingleItem()["title"].ShouldBe("In B");
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
            ("content.article.read", new Core.Authorization.PermissionConstraints { Locales = ["en"] }),
            ("content.article.read", new Core.Authorization.PermissionConstraints { Locales = ["fr-CA"], Fields = ["title"] }));

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
        (await _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().CountAsync("article", null, QueryStatus.Published, Token))).ShouldBe(0);

        Task<long> Count(Principal principal, ContentFilter? filter) =>
            _database.AsAsync(principal, services => services.GetRequiredService<IContentService>().CountAsync("article", filter, QueryStatus.Draft, Token));
    }

    [Fact]
    public async Task AnUnknownTypeIsRefusedEverywhere()
    {
        await Should.ThrowAsync<ValidationException>(() => _database.GetAsync(Editor, Guid.CreateVersion7(), type: "nothing"));
        await Should.ThrowAsync<ValidationException>(() => _database.CreateAsync(Editor, [], "nothing"));
    }
}
