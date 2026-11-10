using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.GraphQL.Tests.BlogHost;

namespace FluentCMS.Plugins.GraphQL.Tests;

[Collection(BlogTests.Name)]
public sealed class GraphQLQueryTests(BlogHost blog)
{
    private const string Delivery = "/api/delivery/v1";

    [Fact]
    public async Task TheBlogIsListedWithItsRelationsBlocksAndMedia()
    {
        using var client = await blog.AdminAsync();

        var data = await client.QueryAsync("""
            {
              articleList(sort: [{ field: TITLE }]) {
                total
                pageSize
                items {
                  id status locale version title slug category publishedOn createdAt body
                  author { id name slug avatar }
                  tags { name slug }
                  hero
                  seo { metaTitle metaDescription }
                  sections {
                    __typename
                    ... on HeroBlock { heading subheading image }
                    ... on QuoteBlock { text attribution }
                    ... on GalleryBlock { caption columns images }
                  }
                }
              }
            }
            """);

        var list = data["articleList"]!;
        list["total"]!.GetValue<long>().ShouldBe(2);
        list["pageSize"]!.GetValue<int>().ShouldBe(20);
        list["items"].Strings("slug").ShouldBe([BlogSeeder.TypedSlug, BlogSeeder.WelcomeSlug]);

        var welcome = list["items"]![1]!;
        welcome["status"]!.GetValue<string>().ShouldBe("PUBLISHED");
        welcome["locale"]!.GetValue<string>().ShouldBe("en");
        welcome["category"]!.GetValue<string>().ShouldBe("NEWS");
        welcome["body"]!["type"]!.GetValue<string>().ShouldBe("doc");
        DateTimeOffset.Parse(welcome["createdAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThan(DateTimeOffset.MinValue);

        // A relation is populated one level at a time, and the media below it with it.
        welcome["author"]!["name"]!.GetValue<string>().ShouldBe("Ada Lovelace");
        welcome["author"]!["avatar"]!["urls"]!["original"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
        welcome["tags"].Strings("slug").ShouldBe([BlogSeeder.DotnetSlug, BlogSeeder.HeadlessSlug]);
        welcome["hero"]!["mimeType"]!.GetValue<string>().ShouldBe("image/png");
        welcome["seo"]!["metaTitle"]!.GetValue<string>().ShouldBe("Welcome to FluentCMS");

        // A block is the member of the union its type names, and the media inside it is populated with the list.
        var sections = welcome["sections"]!.AsArray();
        sections.Select(block => block!["__typename"]!.GetValue<string>()).ShouldBe(["HeroBlock", "QuoteBlock"]);
        sections[0]!["heading"]!.GetValue<string>().ShouldBe("Welcome");
        sections[0]!["image"]!["id"]!.GetValue<string>().ShouldBe(welcome["hero"]!["id"]!.GetValue<string>());
        sections[1]!["text"]!.GetValue<string>().ShouldBe("Content is data first.");
    }

    [Fact]
    public async Task AnEntryIsReadByIdAndByItsUniqueField()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        var bySlug = (await client.QueryAsync($$"""{ article(slug: "{{BlogSeeder.WelcomeSlug}}") { id title author { name } } }"""))["article"]!;
        bySlug["title"]!.GetValue<string>().ShouldBe("Welcome to FluentCMS");
        bySlug["author"]!["name"]!.GetValue<string>().ShouldBe("Ada Lovelace");

        var id = bySlug["id"]!.GetValue<string>();
        var byId = (await client.QueryAsync("query($id: ID) { article(id: $id) { slug tags { name } } }", new { id }))["article"]!;
        byId["slug"]!.GetValue<string>().ShouldBe(BlogSeeder.WelcomeSlug);
        byId["tags"]!.AsArray().Count.ShouldBe(2);

        // An entry that is not there is null, and so is one in a state the caller did not ask for.
        (await client.QueryAsync($$"""{ article(id: "{{Guid.NewGuid():D}}") { id } }"""))["article"].ShouldBeNull();
        (await client.QueryAsync($$"""{ article(slug: "{{BlogSeeder.TourSlug}}") { id } }"""))["article"].ShouldBeNull();

        var (_, both) = await client.FailAsync($$"""{ article(id: "{{id}}", slug: "x") { id } }""");
        both.Codes().ShouldBe(["FCMS1642"]);
        var (_, neither) = await client.FailAsync("{ article { id } }");
        neither.Codes().ShouldBe(["FCMS1642"]);
        var (_, notAnId) = await client.FailAsync("""{ article(id: "nope") { id } }""");
        notAnId.Codes().ShouldBe(["FCMS2204"]);
    }

    [Theory]
    [InlineData("""{ title: { contains: "content" } }""", "filter[title][contains]=content", 1)]
    [InlineData("""{ slug: { in: ["welcome-to-fluentcms", "typed-content-no-magic"] } }""", "filter[slug][in]=welcome-to-fluentcms,typed-content-no-magic", 2)]
    [InlineData("""{ author: { where: { name: { startsWith: "Ada" } } } }""", "filter[author.name][startsWith]=Ada", 1)]
    [InlineData("""{ tags: { where: { slug: { eq: "headless-cms" } } } }""", "filter[tags.slug][eq]=headless-cms", 1)]
    [InlineData("""{ seo: { metaDescription: { notNull: true } } }""", "filter[seo.metaDescription][notNull]=true", 1)]
    [InlineData("""{ sections: { hasBlock: "hero" } }""", "filter[sections][hasBlock]=hero", 1)]
    [InlineData("""{ publishedOn: { null: true } }""", "filter[publishedOn][null]=true", 1)]
    [InlineData("""{ version: { gte: 1 }, status: { eq: PUBLISHED } }""", "filter[version][gte]=1&filter[status][eq]=published", 2)]
    [InlineData("""{ or: [{ slug: { eq: "welcome-to-fluentcms" } }, { seo: { metaTitle: { startsWith: "Typed" } } }] }""", "filter[or][0][slug][eq]=welcome-to-fluentcms&filter[or][1][seo.metaTitle][startsWith]=Typed", 2)]
    [InlineData("""{ not: { title: { startsWith: "Welcome" } } }""", "filter[not][title][startsWith]=Welcome", 1)]
    [InlineData("""{ and: [{ title: { ne: "x" } }, { not: { author: { where: { slug: { eq: "ada-lovelace" } } } } }] }""", "filter[and][0][title][ne]=x&filter[and][1][not][author.slug][eq]=ada-lovelace", 1)]
    public async Task AFilterReturnsTheEntriesOfTheDeliveryApiForTheSameFilter(string filter, string brackets, int matches)
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        var graphql = (await client.QueryAsync($"{{ articleList(filter: {filter}, sort: [{{ field: TITLE }}]) {{ items {{ id }} }} }}"))["articleList"]!["items"].Strings("id");
        var rest = (await RestAsync(client, $"{Delivery}/article?{brackets}&sort=title"))["items"].Strings("id");

        graphql.ShouldBe(rest);
        graphql.Count.ShouldBe(matches);
    }

    [Fact]
    public async Task ASelectFiltersByItsEnumAndARelationByTheIdOfItsTarget()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");
        var welcome = await RestAsync(client, $"{Delivery}/article/by/slug/{BlogSeeder.WelcomeSlug}");
        var category = welcome["category"]!.GetValue<string>();
        var author = welcome["author"]!["id"]!.GetValue<string>();

        var byCategory = (await client.QueryAsync("{ articleList(filter: { category: { in: [NEWS, VIDEO] } }) { items { id } } }"))["articleList"]!["items"].Strings("id");
        byCategory.ShouldBe((await RestAsync(client, $"{Delivery}/article?filter[category][in]={category},Video"))["items"].Strings("id"));
        byCategory.ShouldBe([welcome["id"]!.GetValue<string>()]);

        var byAuthor = (await client.QueryAsync("query($author: ID) { articleList(filter: { author: { eq: $author } }) { items { id } } }", new { author }))["articleList"]!["items"].Strings("id");
        byAuthor.ShouldBe((await RestAsync(client, $"{Delivery}/article?filter[author][eq]={author}"))["items"].Strings("id"));
        byAuthor.ShouldBe([welcome["id"]!.GetValue<string>()]);
    }

    [Fact]
    public async Task OffsetAndCursorPaginationPageThroughACollectionInAStableOrder()
    {
        using var admin = await blog.AdminAsync();
        var prefix = "page-" + Guid.NewGuid().ToString("N")[..8];
        for (var index = 0; index < 7; index++)
        {
            // Every entry has the same name, so the order within the list is the tiebreak of the query.
            await admin.QueryAsync("mutation($input: AuthorInput!) { createAuthor(input: $input) { id } }", new { input = new { name = prefix, slug = $"{prefix}-{index}" } });
        }

        const string Arguments = """filter: { name: { eq: $name } }, status: DRAFT, sort: [{ field: NAME, direction: DESC }], pageSize: 3""";
        var everything = (await admin.QueryAsync($$"""query($name: String) { authorList(filter: { name: { eq: $name } }, status: DRAFT, sort: [{ field: NAME, direction: DESC }], pageSize: 50) { total items { id } } }""", new { name = prefix }))["authorList"]!;
        everything["total"]!.GetValue<long>().ShouldBe(7);
        var all = everything["items"].Strings("id");
        all.Count.ShouldBe(7);

        List<string> byPage = [];
        for (var page = 1; page <= 3; page++)
        {
            var result = (await admin.QueryAsync($$"""query($name: String, $page: Int) { authorList({{Arguments}}, page: $page) { page pageSize items { id } } }""", new { name = prefix, page }))["authorList"]!;
            result["page"]!.GetValue<int>().ShouldBe(page);
            result["pageSize"]!.GetValue<int>().ShouldBe(3);
            byPage.AddRange(result["items"].Strings("id"));
        }

        byPage.ShouldBe(all);

        var byOffset = (await admin.QueryAsync($$"""query($name: String) { authorList({{Arguments}}, offset: 5) { page items { id } } }""", new { name = prefix }))["authorList"]!;
        byOffset["page"].ShouldBeNull();
        byOffset["items"].Strings("id").ShouldBe(all[5..]);

        List<string> byCursor = [];
        string? cursor = null;
        do
        {
            var result = (await admin.QueryAsync($$"""query($name: String, $cursor: String) { authorList({{Arguments}}, cursor: $cursor) { nextCursor items { id } } }""", new { name = prefix, cursor }))["authorList"]!;
            byCursor.AddRange(result["items"].Strings("id"));
            cursor = result["nextCursor"]?.GetValue<string>();
        }
        while (cursor is not null && byCursor.Count < 20);

        byCursor.ShouldBe(all);
    }

    [Fact]
    public async Task LocaleAndFallbackBehaveAsInRest()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        var data = await client.QueryAsync($$"""
            {
              english: article(slug: "{{BlogSeeder.WelcomeSlug}}") { id locale title }
              french: article(slug: "bienvenue-dans-fluentcms", locale: "fr") { id locale title }
              list: articleList(locale: "fr", fallback: NONE) { items { slug locale } }
            }
            """);

        data["english"]!["locale"]!.GetValue<string>().ShouldBe("en");
        data["french"]!["locale"]!.GetValue<string>().ShouldBe("fr");
        data["french"]!["title"]!.GetValue<string>().ShouldBe("Bienvenue dans FluentCMS");
        data["french"]!["id"]!.GetValue<string>().ShouldBe(data["english"]!["id"]!.GetValue<string>());

        var rest = await RestAsync(client, $"{Delivery}/article?locale=fr&fallback=none");
        data["list"]!["items"].Strings("slug").ShouldBe(rest["items"].Strings("slug"));
        data["list"]!["items"].Strings("slug").ShouldBe(["bienvenue-dans-fluentcms"]);

        // The locale of the request is the locale of a field that names none, as it is in REST.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = "{ articleList { items { slug } } }" }),
        };
        request.Headers.Add("X-Cms-Locale", "fr");
        using var response = await client.SendAsync(request, Token);
        var byHeader = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        byHeader["data"]!["articleList"]!["items"].Strings("slug").ShouldContain("bienvenue-dans-fluentcms");

        // A locale the site does not have is refused with the code of REST, and so is every locale at once.
        using var unknown = await client.GetAsync(new Uri($"{Delivery}/article?locale=xx", UriKind.Relative), Token);
        var restCode = JsonNode.Parse(await unknown.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();
        var (noData, errors) = await client.FailAsync("""{ articleList(locale: "xx") { items { id } } }""");
        noData!["articleList"].ShouldBeNull();
        errors.Codes().ShouldBe([restCode]);
        (await client.FailAsync("""{ articleList(locale: "all") { items { id } } }""")).Errors.Codes().ShouldBe(["FCMS2201"]);
    }

    [Fact]
    public async Task StatusBehavesAsInRest()
    {
        using var delivery = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");
        using var preview = await blog.KeyAsync(ApiKeyClass.Preview, "content.*.read");

        // Published is what a field reads unless it asks for drafts, whoever asks.
        (await preview.QueryAsync("{ articleList { items { slug } } }"))["articleList"]!["items"].Strings("slug")
            .ShouldBe([BlogSeeder.TypedSlug, BlogSeeder.WelcomeSlug], ignoreOrder: true);

        var drafts = (await preview.QueryAsync("{ articleList(status: DRAFT) { items { slug status } } }"))["articleList"]!["items"]!;
        drafts.Strings("slug").ShouldContain(BlogSeeder.TourSlug);
        drafts.AsArray().Single(item => item!["slug"]!.GetValue<string>() == BlogSeeder.TourSlug)!["status"]!.GetValue<string>().ShouldBe("DRAFT");
        drafts.Strings("slug").ShouldBe((await RestAsync(preview, $"{Delivery}/article?status=draft"))["items"].Strings("slug"), ignoreOrder: true);

        // A Delivery key reads published entries only: the content service refuses it the drafts, with the code of REST.
        var (data, errors) = await delivery.FailAsync("{ articleList(status: DRAFT) { items { slug } } }");
        data!["articleList"].ShouldBeNull();
        errors.Codes().ShouldBe(["FCMS2209"]);
        (await delivery.FailAsync($$"""{ article(slug: "{{BlogSeeder.TourSlug}}", status: DRAFT) { slug } }""")).Errors.Codes().ShouldBe(["FCMS2209"]);
    }

    [Fact]
    public async Task RichTextIsJsonOrTheFormatAsked()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        var data = await client.QueryAsync($$"""
            {
              json: article(slug: "{{BlogSeeder.WelcomeSlug}}") { body }
              html: article(slug: "{{BlogSeeder.WelcomeSlug}}", richText: HTML) { body }
              markdown: article(slug: "{{BlogSeeder.WelcomeSlug}}", richText: MARKDOWN) { body }
            }
            """);

        data["json"]!["body"]!["content"]!.AsArray().Count.ShouldBeGreaterThan(0);
        data["html"]!["body"]!.GetValue<string>().ShouldBe((await RestAsync(client, $"{Delivery}/article/by/slug/{BlogSeeder.WelcomeSlug}?richText=html"))["body"]!.GetValue<string>());
        data["html"]!["body"]!.GetValue<string>().ShouldContain("<p>");
        data["markdown"]!["body"]!.GetValue<string>().ShouldContain("FluentCMS is a headless CMS for .NET.");
    }

    [Fact]
    public async Task APageSizeOverTheLimitAndAPathDeeperThanTheCapAreRefusedAsInRest()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        using var rest = await client.GetAsync(new Uri($"{Delivery}/article?pageSize=101", UriKind.Relative), Token);
        rest.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonNode.Parse(await rest.Content.ReadAsStringAsync(Token))!;

        var (data, errors) = await client.FailAsync("{ articleList(pageSize: 101) { items { id } } }");
        data!["articleList"].ShouldBeNull();
        errors.Codes().ShouldBe([problem["code"]!.GetValue<string>()]);
        errors[0]!["extensions"]!["code"]!.GetValue<string>().ShouldBe("FCMS2207");
        errors[0]!["extensions"]!["status"]!.GetValue<int>().ShouldBe(400);
        errors[0]!["extensions"]!["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("pageSize");
        errors[0]!["message"]!.GetValue<string>().ShouldBe(problem["detail"]!.GetValue<string>());

        (await client.FailAsync("{ articleList(page: 2, offset: 3) { items { id } } }")).Errors.Codes().ShouldBe(["FCMS2201"]);

        // A search needs an index, which this host has none of, as q= does in REST.
        using var search = await client.GetAsync(new Uri($"{Delivery}/article?q=fluent", UriKind.Relative), Token);
        var searchCode = JsonNode.Parse(await search.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();
        (await client.FailAsync("""{ articleList(search: "fluent") { items { id } } }""")).Errors.Codes().ShouldBe([searchCode]);
        searchCode.ShouldBe("FCMS2211");
    }

    internal static async Task<JsonNode> RestAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }
}
