using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Samples.Blog;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// The query examples of spec 4.4 over HTTP, against the Blog seed: two published articles in English, one of them also
// in French, and drafts that only a preview principal sees.
public sealed class DeliveryApiTests(DeliveryFixture fixture) : IClassFixture<DeliveryFixture>
{
    private const string Welcome = BlogSeeder.WelcomeSlug;
    private const string Typed = BlogSeeder.TypedSlug;

    [Fact]
    public async Task AListReturnsThePublishedEntriesInTheFlatShape()
    {
        var (status, body) = await fixture.GetAsync("article");

        status.ShouldBe(HttpStatusCode.OK);
        Slugs(body).ShouldBe([Welcome, Typed], ignoreOrder: true);
        body["total"]!.GetValue<long>().ShouldBe(2);
        body["page"]!.GetValue<int>().ShouldBe(1);
        body["pageSize"]!.GetValue<int>().ShouldBe(20);

        var entry = Item(body, Welcome);
        entry["status"]!.GetValue<string>().ShouldBe("published");
        entry["locale"]!.GetValue<string>().ShouldBe("en");
        entry["version"]!.GetValue<int>().ShouldBe(1);
        entry["author"]!.AsObject().Select(pair => pair.Key).ShouldBe(["id", "type"]);
        entry["sections"]![0]!["type"]!.GetValue<string>().ShouldBe("hero");
        entry.AsObject().ContainsKey("sys").ShouldBeFalse();
    }

    [Theory]
    [InlineData("filter[category][eq]=news", new[] { Welcome })]
    [InlineData("filter[category][in]=news,opinion", new[] { Welcome, Typed })]
    [InlineData("filter[author.name][contains]=Grace", new[] { Typed })]
    [InlineData("filter[or][0][title][startsWith]=W&filter[or][1][title][startsWith]=T", new[] { Welcome, Typed })]
    [InlineData("filter[not][title][startsWith]=W", new[] { Typed })]
    [InlineData("filter[sections][hasBlock]=hero", new[] { Welcome })]
    [InlineData("filter[sections][hasBlock]=quote", new[] { Welcome, Typed })]
    [InlineData("filter[publishedOn][null]=true", new[] { Typed })]
    [InlineData("filter[category][eq]=video", new string[0])]
    public async Task AFilterSelectsTheEntries(string query, string[] slugs)
    {
        var (status, body) = await fixture.GetAsync("article?" + query);

        status.ShouldBe(HttpStatusCode.OK);
        Slugs(body).ShouldBe(slugs, ignoreOrder: true);
    }

    [Theory]
    [InlineData("sort=title", new[] { Typed, Welcome })]
    [InlineData("sort=-title", new[] { Welcome, Typed })]
    [InlineData("sort=-publishedOn,title", new[] { Welcome, Typed })]
    public async Task ASortOrdersTheEntries(string query, string[] slugs)
    {
        var (_, body) = await fixture.GetAsync("article?" + query);

        Slugs(body).ShouldBe(slugs);
    }

    [Fact]
    public async Task PagesOffsetsAndCursorsWalkTheList()
    {
        var (_, page) = await fixture.GetAsync("article?sort=title&page=2&pageSize=1");
        Slugs(page).ShouldBe([Welcome]);
        page["page"]!.GetValue<int>().ShouldBe(2);
        page["total"]!.GetValue<long>().ShouldBe(2);

        var (_, offset) = await fixture.GetAsync("article?sort=title&offset=1&limit=1&withTotal=false");
        Slugs(offset).ShouldBe([Welcome]);
        offset.AsObject().ContainsKey("total").ShouldBeFalse();

        var (_, first) = await fixture.GetAsync("article?sort=title&pageSize=1");
        Slugs(first).ShouldBe([Typed]);
        var cursor = first["nextCursor"]!.GetValue<string>();

        var (_, second) = await fixture.GetAsync($"article?sort=title&pageSize=1&cursor={Uri.EscapeDataString(cursor)}");
        Slugs(second).ShouldBe([Welcome]);
        second.AsObject().ContainsKey("nextCursor").ShouldBeFalse();
    }

    [Fact]
    public async Task FieldsSelectAndPopulateInlines()
    {
        var (_, selected) = await fixture.GetAsync("article?fields=title,slug");
        Item(selected, Welcome).AsObject().Select(pair => pair.Key).ShouldBe(["id", "title", "slug"]);

        var (_, populated) = await fixture.GetAsync("article?populate=author,tags");
        var welcome = Item(populated, Welcome);
        welcome["author"]!["name"]!.GetValue<string>().ShouldBe("Ada Lovelace");
        welcome["tags"]!.AsArray().Select(tag => tag!["name"]!.GetValue<string>()).ShouldBe(["dotnet", "headless cms"]);

        var (_, both) = await fixture.GetAsync("article?fields=title,author.name&populate=author");
        Item(both, "Welcome to FluentCMS", "title")["author"]!.AsObject().Select(pair => pair.Key).ShouldBe(["id", "type", "name"]);
    }

    [Fact]
    public async Task RichTextIsRenderedOnRequest()
    {
        var (_, json) = await fixture.GetAsync($"article/by/slug/{Welcome}");
        var (_, html) = await fixture.GetAsync($"article/by/slug/{Welcome}?richText=html");

        json["body"]!["type"]!.GetValue<string>().ShouldBe("doc");
        html["body"]!.GetValue<string>().ShouldBe("<p>FluentCMS is a headless CMS for .NET.</p>");
    }

    [Fact]
    public async Task AnEntryIsFoundByIdAndByAUniqueField()
    {
        var (_, bySlug) = await fixture.GetAsync($"article/by/slug/{Welcome}");
        var id = bySlug["id"]!.GetValue<string>();

        var (status, byId) = await fixture.GetAsync($"article/{id}?fields=title");

        status.ShouldBe(HttpStatusCode.OK);
        byId["title"]!.GetValue<string>().ShouldBe("Welcome to FluentCMS");
    }

    [Theory]
    [InlineData("article/by/slug/no-such-article", HttpStatusCode.NotFound, "FCMS3104", "value")]
    [InlineData("article/by/title/Welcome", HttpStatusCode.BadRequest, "FCMS3123", "field")]
    [InlineData("article/0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f", HttpStatusCode.NotFound, "FCMS3104", null)]
    [InlineData("article/not-an-id", HttpStatusCode.BadRequest, "FCMS2204", "id")]
    [InlineData("article/0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f?sort=title", HttpStatusCode.BadRequest, "FCMS2201", "sort")]
    [InlineData("nothing", HttpStatusCode.BadRequest, "FCMS2202", "type")]
    public async Task ASingleEntryThatCannotBeServedIsAProblem(string path, HttpStatusCode expected, string code, string? errorPath)
    {
        var (status, body) = await fixture.GetAsync(path);

        status.ShouldBe(expected);
        body["code"]!.GetValue<string>().ShouldBe(code);
        if (errorPath is not null)
        {
            body["errors"]![0]!["path"]!.GetValue<string>().ShouldBe(errorPath);
        }
    }

    [Theory]
    [InlineData("filter[title][$foo]=x", "FCMS2203")]
    [InlineData("filter[title]=x", "FCMS2201")]
    [InlineData("filter[nope.part][eq]=x", "FCMS2202")]
    [InlineData("filter[publishedOn][between]=2026-01-01", "FCMS2204")]
    public async Task AMalformedFilterIsAProblemOnItsParameter(string query, string code)
    {
        var (status, body) = await fixture.GetAsync("article?" + query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body["code"]!.GetValue<string>().ShouldBe(code);
        body["type"]!.GetValue<string>().ShouldBe("urn:fluentcms:error:" + code);
        var parameter = Uri.UnescapeDataString(query[..query.IndexOf('=', StringComparison.Ordinal)]);
        body["errors"]![0]!["path"]!.GetValue<string>().ShouldBe(code == "FCMS2202" ? "nope.part" : parameter);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=many", "pageSize")]
    [InlineData("status=archived", "status")]
    [InlineData("richText=pdf", "richText")]
    [InlineData("depth=2", "depth")]
    [InlineData("unknown=1", "unknown")]
    public async Task AMalformedParameterIsAProblemOnItsName(string query, string path)
    {
        var (status, body) = await fixture.GetAsync("article?" + query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body["errors"]![0]!["path"]!.GetValue<string>().ShouldBe(path);
    }

    [Fact]
    public async Task AFrenchReadServesTheFrenchEntriesOnly()
    {
        var (_, byParameter) = await fixture.GetAsync("article?locale=fr");
        var (_, byHeader) = await fixture.GetAsync("article", ("X-Cms-Locale", "fr"));

        Slugs(byParameter).ShouldBe(["bienvenue-dans-fluentcms"]);
        Slugs(byHeader).ShouldBe(["bienvenue-dans-fluentcms"]);
        byParameter["items"]![0]!["locale"]!.GetValue<string>().ShouldBe("fr");
    }

    [Fact]
    public async Task LocaleAllReturnsEveryLocaleAsAMap()
    {
        var (status, body) = await fixture.GetAsync($"article/by/slug/{Welcome}?locale=all");

        status.ShouldBe(HttpStatusCode.OK);
        var locales = body["locales"]!.AsObject();
        locales.Select(pair => pair.Key).ShouldBe(["en", "fr"], ignoreOrder: true);
        locales["fr"]!["title"]!.GetValue<string>().ShouldBe("Bienvenue dans FluentCMS");
        locales["en"]!["title"]!.GetValue<string>().ShouldBe("Welcome to FluentCMS");
    }

    [Fact]
    public async Task AnUnknownLocaleIsAProblem()
    {
        var (status, body) = await fixture.GetAsync("article?locale=de");

        status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        body["code"]!.GetValue<string>().ShouldBe("FCMS2312");
    }

    [Fact]
    public async Task DraftsAreRefusedAnonymously()
    {
        var (status, body) = await fixture.GetAsync("article?status=draft");

        status.ShouldBe(HttpStatusCode.Forbidden);
        body["code"]!.GetValue<string>().ShouldBe("FCMS2209");
    }

    [Theory]
    [InlineData("preview", HttpStatusCode.OK)]
    [InlineData("draft", HttpStatusCode.OK)]
    [InlineData("delivery", HttpStatusCode.Forbidden)]
    public async Task DraftsNeedThePreviewOrDraftScope(string scope, HttpStatusCode expected)
    {
        var (status, body) = await fixture.GetAsync(
            "article?status=draft",
            (TestScheme.KindHeader, nameof(PrincipalKind.ApiKey)),
            (TestScheme.RolesHeader, DefaultSite.PublicRole.ToString()),
            (TestScheme.ScopeHeader, scope));

        status.ShouldBe(expected);
        if (expected == HttpStatusCode.OK)
        {
            Slugs(body).ShouldBe([Welcome, Typed, BlogSeeder.TourSlug, BlogSeeder.ScheduledSlug], ignoreOrder: true);
            Item(body, BlogSeeder.TourSlug)["status"]!.GetValue<string>().ShouldBe("draft");
        }
    }

    [Fact]
    public async Task AnonymousReadsGetWhatThePublicRoleGrants()
    {
        var (homepage, _) = await fixture.GetAsync("homepage");
        homepage.ShouldBe(HttpStatusCode.BadRequest);

        var (_, authors) = await fixture.GetAsync("author");
        authors["items"]!.AsArray().ShouldAllBe(author => author!.AsObject().ContainsKey("name") && !author.AsObject().ContainsKey("bio"));

        var (_, populated) = await fixture.GetAsync("article?populate=author");
        Item(populated, Welcome)["author"]!.AsObject().ContainsKey("bio").ShouldBeFalse();

        var (otherSite, _) = await fixture.GetAsync("article", ("X-Cms-Site", "other"));
        otherSite.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ASingletonReturnsItsEntry()
    {
        var (status, body) = await fixture.GetAsync("homepage", DeliveryFixture.EditorKey());

        status.ShouldBe(HttpStatusCode.OK);
        body["headline"]!.GetValue<string>().ShouldBe(DeliveryFixture.Headline);
        body.AsObject().ContainsKey("items").ShouldBeFalse();
    }

    [Fact]
    public async Task AnUnboundRequestNamesItsSiteByHeaderOrParameter()
    {
        var key = DeliveryFixture.EditorKey();

        Slugs((await fixture.GetAsync("article", key)).Body).Count.ShouldBe(2);
        Slugs((await fixture.GetAsync("article", [.. key, ("X-Cms-Site", "other")])).Body).ShouldBeEmpty();
        Slugs((await fixture.GetAsync($"article?site={fixture.OtherSite}", key)).Body).ShouldBeEmpty();
        Slugs((await fixture.GetAsync("article?site=default", [.. key, ("X-Cms-Site", "other")])).Body).ShouldBeEmpty();

        var (missing, body) = await fixture.GetAsync("article?site=nowhere", key);
        missing.ShouldBe(HttpStatusCode.NotFound);
        body["code"]!.GetValue<string>().ShouldBe("FCMS2301");
    }

    [Fact]
    public async Task AKeyBoundToOneSiteReadsThatSite()
    {
        var bound = DeliveryFixture.EditorKey(fixture.OtherSite);

        Slugs((await fixture.GetAsync("article", bound)).Body).ShouldBeEmpty();
        Slugs((await fixture.GetAsync("article?site=other", bound)).Body).ShouldBeEmpty();

        var (status, body) = await fixture.GetAsync("article", [.. bound, ("X-Cms-Site", "default")]);
        status.ShouldBe(HttpStatusCode.Forbidden);
        body["code"]!.GetValue<string>().ShouldBe("FCMS3121");
        body["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("X-Cms-Site");
    }

    [Fact]
    public async Task AKeyBoundToSeveralSitesReadsTheOneNamedOrTheDefault()
    {
        var withDefault = DeliveryFixture.EditorKey(fixture.OtherSite, DefaultSite.Id);
        Slugs((await fixture.GetAsync("article", withDefault)).Body).Count.ShouldBe(2);
        Slugs((await fixture.GetAsync("article?site=other", withDefault)).Body).ShouldBeEmpty();

        var withoutDefault = DeliveryFixture.EditorKey(fixture.OtherSite, fixture.ThirdSite);
        Slugs((await fixture.GetAsync("article?site=third", withoutDefault)).Body).ShouldBeEmpty();

        var (unnamed, body) = await fixture.GetAsync("article", withoutDefault);
        unnamed.ShouldBe(HttpStatusCode.BadRequest);
        body["code"]!.GetValue<string>().ShouldBe("FCMS3122");

        var (outside, refused) = await fixture.GetAsync("article?site=default", withoutDefault);
        outside.ShouldBe(HttpStatusCode.Forbidden);
        refused["code"]!.GetValue<string>().ShouldBe("FCMS3121");
        refused["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("site");
    }

    private static List<string> Slugs(JsonNode body) =>
        [.. body["items"]!.AsArray().Select(item => item!["slug"]!.GetValue<string>())];

    private static JsonNode Item(JsonNode body, string value, string field = "slug") =>
        body["items"]!.AsArray().Single(item => item![field]!.GetValue<string>() == value)!;
}
