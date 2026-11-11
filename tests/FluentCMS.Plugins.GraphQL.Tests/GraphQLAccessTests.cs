using System.Net;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.GraphQL.Tests.BlogHost;

namespace FluentCMS.Plugins.GraphQL.Tests;

[Collection(BlogTests.Name)]
public sealed class GraphQLAccessTests(BlogHost blog)
{
    private const string CreateAuthor = "mutation { createAuthor(input: { name: \"Nobody\", slug: \"nobody\" }) { id } }";

    [Fact]
    public async Task ADeliveryKeyAndAnAnonymousCallerAreRefusedEveryMutation()
    {
        // Even with a role that holds the writes: a Delivery key is not for managing, which the content service decides.
        using var delivery = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.*");
        using var anonymous = blog.Anonymous();
        var id = (await delivery.QueryAsync($$"""{ author(slug: "{{BlogSeeder.AdaSlug}}") { id } }"""))["author"]!["id"]!.GetValue<string>();

        var (data, errors) = await delivery.FailAsync(CreateAuthor);
        data!["createAuthor"].ShouldBeNull();
        errors.Codes().ShouldBe(["FCMS3103"]);
        errors[0]!["extensions"]!["status"]!.GetValue<int>().ShouldBe(403);

        foreach (var mutation in (string[])[
            "updateAuthor(id: $id, input: { name: \"Changed\" }, expectedVersion: 1) { id }",
            "publishAuthor(id: $id) { entry { id } }",
            "unpublishAuthor(id: $id) { id }",
            "deleteAuthor(id: $id)"])
        {
            (await delivery.FailAsync($"mutation($id: ID!) {{ {mutation} }}", new { id })).Errors.Codes().ShouldBe(["FCMS3103"], mutation);
        }

        // An anonymous caller is asked to sign in, as REST asks it with a 401.
        var (_, unauthorized) = await anonymous.FailAsync(CreateAuthor);
        unauthorized.Codes().ShouldBe(["FCMS3102"]);
        unauthorized[0]!["extensions"]!["status"]!.GetValue<int>().ShouldBe(401);

        (await delivery.QueryAsync($$"""{ author(slug: "{{BlogSeeder.AdaSlug}}") { name } }"""))["author"]!["name"]!.GetValue<string>().ShouldBe("Ada Lovelace");
    }

    [Fact]
    public async Task AMembersTokenReadsWhatItsGroupsRolesAllowAndIsRefusedEveryMutation()
    {
        // Even in a group bound to Super Admin: a member's token has no scope that manages, so it reads published
        // content and writes nothing.
        var group = $"readers-{Guid.NewGuid():N}"[..20];
        await blog.Host.CreateMemberGroupAsync(group, Core.Authorization.BuiltInRoles.SuperAdminName, Token);
        using var member = await blog.Host.AsMemberAsync(await blog.Host.CreateMemberAsync(groups: [group], cancellationToken: Token));
        using var stranger = await blog.Host.AsMemberAsync(await blog.Host.CreateMemberAsync(cancellationToken: Token));

        var author = (await member.QueryAsync($$"""{ author(slug: "{{BlogSeeder.AdaSlug}}") { id name } }"""))["author"]!;
        author["name"]!.GetValue<string>().ShouldBe("Ada Lovelace");
        var id = author["id"]!.GetValue<string>();

        var (data, errors) = await member.FailAsync(CreateAuthor);
        data!["createAuthor"].ShouldBeNull();
        errors.Codes().ShouldBe(["FCMS3103"]);
        errors[0]!["extensions"]!["status"]!.GetValue<int>().ShouldBe(403);

        foreach (var mutation in (string[])[
            "updateAuthor(id: $id, input: { name: \"Changed\" }, expectedVersion: 1) { id }",
            "publishAuthor(id: $id) { entry { id } }",
            "unpublishAuthor(id: $id) { id }",
            "deleteAuthor(id: $id)"])
        {
            (await member.FailAsync($"mutation($id: ID!) {{ {mutation} }}", new { id })).Errors.Codes().ShouldBe(["FCMS3103"], mutation);
        }

        // A member in no group holds the Public and the Member role, which grant nothing here.
        var (nothing, refused) = await stranger.FailAsync("{ articleList { items { title } } }");
        nothing!["articleList"].ShouldBeNull();
        refused.Codes().ShouldBe(["FCMS3104"]);
    }

    [Fact]
    public async Task AnAnonymousCallerReadsWhatThePublicRoleMayRead()
    {
        using var anonymous = blog.Anonymous();

        // The seeded Public role holds no content, so every type is one the caller may not read.
        var (data, errors) = await anonymous.FailAsync("{ articleList { items { title } } }");
        data!["articleList"].ShouldBeNull();
        errors.Codes().ShouldBe(["FCMS3104"]);
    }

    [Fact]
    public async Task ATypeThePrincipalMayNotReadIsNullWithTheCodeOfRest()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.article.read");

        using var rest = await client.GetAsync(new Uri("/api/delivery/v1/author", UriKind.Relative), Token);
        rest.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var restCode = System.Text.Json.Nodes.JsonNode.Parse(await rest.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();

        var (data, errors) = await client.FailAsync($$"""
            {
              authorList { items { name } }
              author(slug: "{{BlogSeeder.AdaSlug}}") { name }
              articleList(sort: [{ field: TITLE }]) { items { title author { name } tags { name } } }
            }
            """);

        // The root fields of the type are null, and so is a relation to it, each with one error; what the principal may
        // read is answered beside them.
        data!["authorList"].ShouldBeNull();
        data["author"].ShouldBeNull();
        var articles = data["articleList"]!["items"]!.AsArray();
        articles.Select(article => article!["title"]!.GetValue<string>()).ShouldBe(["Typed content, no magic", "Welcome to FluentCMS"]);
        articles.ShouldAllBe(article => article!["author"] == null);
        articles.ShouldAllBe(article => article!["tags"] == null);

        errors.Codes().ShouldAllBe(code => code == restCode);
        restCode.ShouldBe("FCMS3104");
        errors.Select(error => string.Join('.', error!["path"]!.AsArray().Select(part => part!.ToString())))
            .ShouldBe(["authorList", "author", "articleList.items.0.author", "articleList.items.0.tags"], ignoreOrder: true);
    }

    [Fact]
    public async Task AFieldThePrincipalMayNotReadIsNullWithTheCodeOfRest()
    {
        string[] ofArticle = ["title", "slug", "author"];
        string[] ofAuthor = ["name"];
        using var client = await blog.KeyWithGrantsAsync(
            ApiKeyClass.Delivery,
            new { permission = "content.article.read", constraints = new { fields = ofArticle } },
            new { permission = "content.author.read", constraints = new { fields = ofAuthor } });

        using var rest = await client.GetAsync(new Uri("/api/delivery/v1/article?fields=title,body", UriKind.Relative), Token);
        rest.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var restCode = System.Text.Json.Nodes.JsonNode.Parse(await rest.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();

        var (data, errors) = await client.FailAsync("""
            {
              articleList(sort: [{ field: TITLE }]) {
                items { title slug body category seo { metaTitle } author { name slug } }
              }
            }
            """);

        var articles = data!["articleList"]!["items"]!.AsArray();
        articles.Count.ShouldBe(2);
        foreach (var article in articles)
        {
            article!["title"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
            article["slug"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
            article["body"].ShouldBeNull();
            article["category"].ShouldBeNull();
            article["seo"].ShouldBeNull();
            article["author"]!["name"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
            article["author"]!["slug"].ShouldBeNull();
        }

        // One error per field, at the first entry that has it, with the code REST gives a query that names the field.
        restCode.ShouldBe("FCMS2202");
        errors.Codes().ShouldAllBe(code => code == restCode);
        errors.Select(error => error!["path"]!.AsArray().Last()!.ToString()).ShouldBe(["body", "category", "seo", "slug"], ignoreOrder: true);
        errors.Select(error => error!["message"]!.GetValue<string>()).ShouldContain("'body' is not a field of article that can be read.");

        // A filter or a sort on such a field is the content service's to refuse, as in REST.
        (await client.FailAsync("{ articleList(filter: { category: { eq: NEWS } }) { items { title } } }")).Errors.Codes().ShouldBe(["FCMS2202"]);
    }

    [Fact]
    public async Task TheEndpointTakesTheRateLimitOfTheCallersClass()
    {
        using var anonymous = blog.Anonymous();
        using var delivery = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");
        using var admin = await blog.AdminAsync();

        (await LimitAsync(anonymous)).ShouldBe("600");
        (await LimitAsync(delivery)).ShouldBe("600");
        (await LimitAsync(admin)).ShouldBe("300");

        static async Task<string> LimitAsync(HttpClient client)
        {
            using var response = await client.PostAsync(
                new Uri(Endpoint, UriKind.Relative),
                System.Net.Http.Json.JsonContent.Create(new { query = "{ __typename }" }),
                Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl?.NoStore.ShouldBe(true);
            return response.Headers.GetValues("RateLimit-Limit").Single();
        }
    }
}
