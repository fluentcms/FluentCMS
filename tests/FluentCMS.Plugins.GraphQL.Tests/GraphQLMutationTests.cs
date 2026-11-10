using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Events;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.GraphQL.Tests.BlogHost;

namespace FluentCMS.Plugins.GraphQL.Tests;

[Collection(BlogTests.Name)]
public sealed class GraphQLMutationTests(BlogHost blog)
{
    private const string Management = "/api/management/v1";

    [Fact]
    public async Task TheMutationsDoWhatTheManagementApiDoesAndEachWriteHasItsVersionAndActivityRow()
    {
        using var admin = await blog.AdminAsync();
        var slug = "author-" + Guid.NewGuid().ToString("N")[..8];

        var created = (await admin.QueryAsync(
            "mutation($input: AuthorInput!) { createAuthor(input: $input) { id status version name slug } }",
            new { input = new { name = "Margaret Hamilton", slug } }))["createAuthor"]!;
        var id = created["id"]!.GetValue<string>();
        created["status"]!.GetValue<string>().ShouldBe("DRAFT");
        created["version"]!.GetValue<int>().ShouldBe(1);

        // An update is a merge patch: the slug it does not name keeps its value.
        var updated = (await admin.QueryAsync(
            "mutation($id: ID!, $input: AuthorInput!) { updateAuthor(id: $id, input: $input, expectedVersion: 1) { version name slug } }",
            new { id, input = new { name = "Margaret H. Hamilton" } }))["updateAuthor"]!;
        updated["version"]!.GetValue<int>().ShouldBe(2);
        updated["name"]!.GetValue<string>().ShouldBe("Margaret H. Hamilton");
        updated["slug"]!.GetValue<string>().ShouldBe(slug);

        var published = (await admin.QueryAsync("mutation($id: ID!) { publishAuthor(id: $id, expectedVersion: 2) { entry { status publishedAt } warnings { code path message } } }", new { id }))["publishAuthor"]!;
        published["entry"]!["status"]!.GetValue<string>().ShouldBe("PUBLISHED");
        published["entry"]!["publishedAt"].ShouldNotBeNull();
        published["warnings"]!.AsArray().ShouldBeEmpty();

        // What the mutations wrote is what the Management API reads, and the Delivery API serves it once it is published.
        var rest = await GraphQLQueryTests.RestAsync(admin, $"{Management}/content/author/{id}");
        rest["name"]!.GetValue<string>().ShouldBe("Margaret H. Hamilton");
        rest["status"]!.GetValue<string>().ShouldBe("published");
        (await GraphQLQueryTests.RestAsync(admin, $"/api/delivery/v1/author/{id}"))["slug"]!.GetValue<string>().ShouldBe(slug);

        // Each save is a version of the entry.
        (await GraphQLQueryTests.RestAsync(admin, $"{Management}/content/author/{id}/versions"))["items"]!.AsArray().Count.ShouldBe(2);

        var unpublished = (await admin.QueryAsync("mutation($id: ID!) { unpublishAuthor(id: $id) { status } }", new { id }))["unpublishAuthor"]!;
        unpublished["status"]!.GetValue<string>().ShouldNotBe("PUBLISHED");
        (await admin.QueryAsync("query($id: ID) { author(id: $id) { id } }", new { id }))["author"].ShouldBeNull();

        (await admin.QueryAsync("mutation($id: ID!) { deleteAuthor(id: $id) }", new { id }))["deleteAuthor"]!.GetValue<string>().ShouldBe(id);
        using (var trashed = await admin.GetAsync(new Uri($"{Management}/content/author/{id}", UriKind.Relative), Token))
        {
            trashed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await GraphQLQueryTests.RestAsync(admin, $"{Management}/content/author/trash"))["items"].Strings("id").ShouldContain(id);

        // The writes are the content service's, so each has its activity row, with the API as origin.
        while (await blog.Host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        var activity = (await GraphQLQueryTests.RestAsync(admin, $"{Management}/activity?targetId={id}"))["items"]!.AsArray();
        activity.Select(row => row!["action"]!.GetValue<string>()).ShouldBe(
            ["content.draft_saved", "content.draft_saved", "content.published", "content.unpublished", "content.trashed"],
            ignoreOrder: true);
        activity.Select(row => row!["origin"]!.GetValue<string>()).Distinct().ShouldBe(["api"], Case.Insensitive);
    }

    [Fact]
    public async Task AStaleVersionIsRefusedWithTheCodeOfRestAndTheCurrentEntry()
    {
        using var admin = await blog.AdminAsync();
        var slug = "author-" + Guid.NewGuid().ToString("N")[..8];
        var id = (await admin.QueryAsync("mutation($input: AuthorInput!) { createAuthor(input: $input) { id } }", new { input = new { name = "Stale", slug } }))["createAuthor"]!["id"]!.GetValue<string>();
        await admin.QueryAsync("mutation($id: ID!) { updateAuthor(id: $id, input: { name: \"Fresh\" }, expectedVersion: 1) { version } }", new { id });

        var (data, errors) = await admin.FailAsync("mutation($id: ID!) { updateAuthor(id: $id, input: { name: \"Late\" }, expectedVersion: 1) { version } }", new { id });

        data!["updateAuthor"].ShouldBeNull();
        errors.Codes().ShouldBe(["FCMS3105"]);
        var extensions = errors[0]!["extensions"]!;
        extensions["status"]!.GetValue<int>().ShouldBe(412);
        extensions["current"]!["version"]!.GetValue<int>().ShouldBe(2);
        extensions["current"]!["name"]!.GetValue<string>().ShouldBe("Fresh");
        errors[0]!["path"]!.AsArray().Select(part => part!.ToString()).ShouldBe(["updateAuthor"]);

        // The Management API refuses the same write with the same code.
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"{Management}/content/author/{id}", UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "Late" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var rest = await admin.SendAsync(request, Token);
        rest.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        JsonNode.Parse(await rest.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>().ShouldBe("FCMS3105");

        (await admin.FailAsync("mutation($id: ID!) { publishAuthor(id: $id, expectedVersion: 1) { entry { id } } }", new { id })).Errors.Codes().ShouldBe(["FCMS3105"]);
    }

    [Fact]
    public async Task AValidationErrorCarriesThePathOfItsField()
    {
        using var admin = await blog.AdminAsync();

        var (data, errors) = await admin.FailAsync(
            "mutation($input: AuthorInput!) { createAuthor(input: $input) { id } }",
            new { input = new { name = "Named", slug = "Not A Slug!" } });

        data!["createAuthor"].ShouldBeNull();
        var extensions = errors.Single()!["extensions"]!;
        extensions["status"]!.GetValue<int>().ShouldBe(422);
        var problems = extensions["errors"]!.AsArray();
        problems.Select(problem => problem!["path"]!.GetValue<string>()).ShouldBe(["slug"]);
        problems[0]!["code"]!.GetValue<string>().ShouldBe(errors[0].Code());
        errors[0].Code().ShouldStartWith("FCMS21");

        // No answer carries a stack trace, whatever fails.
        errors.ToJsonString().ShouldNotContain("stackTrace");
        errors.ToJsonString().ShouldNotContain("   at ");
    }

    [Fact]
    public async Task AnArticleIsWrittenWithItsRelationsSelectComponentAndBlocks()
    {
        using var admin = await blog.AdminAsync();
        var seeded = (await admin.QueryAsync($$"""{ article(slug: "{{BlogSeeder.WelcomeSlug}}") { author { id } tags { id } hero } }"""))["article"]!;
        var author = seeded["author"]!["id"]!.GetValue<string>();
        var tags = seeded["tags"].Strings("id");
        var hero = seeded["hero"]!["id"]!.GetValue<string>();
        var slug = "article-" + Guid.NewGuid().ToString("N")[..8];

        var created = (await admin.QueryAsync(
            """
            mutation($input: ArticleInput!) {
              createArticle(input: $input) {
                id version title category
                author { name }
                tags { slug }
                hero
                seo { metaTitle }
                sections { __typename ... on QuoteBlock { text } ... on HeroBlock { heading image } }
              }
            }
            """,
            new
            {
                input = new
                {
                    title = "Written through GraphQL",
                    slug,
                    author,
                    tags,
                    hero,
                    category = "VIDEO",
                    videoUrl = "https://example.com/video",
                    seo = new { metaTitle = "Written" },
                    body = new { type = "doc", content = new[] { new { type = "paragraph" } } },
                    sections = new object[]
                    {
                        new { type = "quote", text = "Typed all the way down." },
                        new { type = "hero", heading = "Hello", image = hero },
                    },
                },
            }))["createArticle"]!;

        // The selection goes into relations and media, so the answer is the entry read again with them populated.
        created["category"]!.GetValue<string>().ShouldBe("VIDEO");
        created["author"]!["name"]!.GetValue<string>().ShouldBe("Ada Lovelace");
        created["tags"].Strings("slug").ShouldBe([BlogSeeder.DotnetSlug, BlogSeeder.HeadlessSlug]);
        created["hero"]!["id"]!.GetValue<string>().ShouldBe(hero);
        created["seo"]!["metaTitle"]!.GetValue<string>().ShouldBe("Written");
        created["sections"]!.AsArray().Select(block => block!["__typename"]!.GetValue<string>()).ShouldBe(["QuoteBlock", "HeroBlock"]);
        created["sections"]![1]!["image"]!["id"]!.GetValue<string>().ShouldBe(hero);

        var id = created["id"]!.GetValue<string>();
        var rest = await GraphQLQueryTests.RestAsync(admin, $"{Management}/content/article/{id}");
        rest["title"]!.GetValue<string>().ShouldBe("Written through GraphQL");
        rest["sections"]!.AsArray().Count.ShouldBe(2);

        // A locale the entry does not have yet is added by an update at version 0, as PATCH with If-Match "0" adds it.
        var french = (await admin.QueryAsync(
            "mutation($id: ID!) { updateArticle(id: $id, locale: \"fr\", expectedVersion: 0, input: { title: \"Écrit avec GraphQL\", slug: \"ecrit-avec-graphql\" }) { locale version title } }",
            new { id }))["updateArticle"]!;
        french["locale"]!.GetValue<string>().ShouldBe("fr");
        french["title"]!.GetValue<string>().ShouldBe("Écrit avec GraphQL");
        (await GraphQLQueryTests.RestAsync(admin, $"{Management}/content/article/{id}?locale=fr"))["slug"]!.GetValue<string>().ShouldBe("ecrit-avec-graphql");
    }
}
