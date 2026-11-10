using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Samples.Blog;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.GraphQL.Tests.BlogHost;

namespace FluentCMS.Plugins.GraphQL.Tests;

// The Blog host as Production has it: introspection is not open, and the limits are small enough to reach. A plugin's
// schema contributor is registered beside it.
public sealed class GuardedHost() : BlogHost(
    options =>
    {
        options.OpenIntrospection = false;
        options.MaxDepth = 3;
        options.MaxFieldCost = 60;
        options.MaxRequestSize = 4096;
    },
    services => services.AddSingleton<IGraphQLSchemaContributor, GreetingContributor>());

public sealed class GraphQLGuardTests(GuardedHost blog) : IClassFixture<GuardedHost>
{
    private const string Introspection = "{ __schema { queryType { name } types { name } } }";

    [Fact]
    public async Task IntrospectionIsRefusedToAnAnonymousCallerAndAnsweredToOneWithSchemaRead()
    {
        using var anonymous = blog.Anonymous();
        using var delivery = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read", "schema.read");
        using var editor = await blog.KeyAsync(ApiKeyClass.Management, "content.*.*");
        using var reader = await blog.KeyAsync(ApiKeyClass.Management, "schema.read");

        foreach (var refused in (HttpClient[])[anonymous, delivery, editor])
        {
            var (status, body) = await refused.SendAsync(Introspection);
            status.ShouldBe(HttpStatusCode.BadRequest);
            body["data"].ShouldBeNull();
            body["errors"]!.AsArray().Codes().Distinct().ShouldBe(["FCMS1643"]);
            (await refused.FailAsync("""{ __type(name: "Article") { name } }""")).Errors.Codes().Distinct().ShouldBe(["FCMS1643"]);
        }

        // The one schema names every type, including those the caller holds no content permission for.
        var schema = (await reader.QueryAsync(Introspection))["__schema"]!;
        schema["queryType"]!["name"]!.GetValue<string>().ShouldBe("Query");
        string[] expected = ["Article", "Author", "Tag", "SeoComponent", "HeroBlock", "ArticleSectionsBlock", "ArticleCategory", "ArticleFilter", "ArticleInput", "Mutation"];
        expected.ShouldBeSubsetOf(schema["types"].Strings("name"));
        (await reader.FailAsync("{ articleList { items { title } } }")).Errors.Codes().ShouldBe(["FCMS3104"]);

        // The name of a type is not introspection.
        (await anonymous.QueryAsync("{ __typename }"))["__typename"]!.GetValue<string>().ShouldBe("Query");
    }

    [Fact]
    public async Task AQueryOverTheDepthOrTheCostLimitIsRefusedBeforeItRuns()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        (await client.QueryAsync("{ articleList { items { title } } }"))["articleList"]!["items"]!.AsArray().Count.ShouldBe(2);

        var (deepStatus, deep) = await client.SendAsync("{ articleList { items { author { name } } } }");
        deepStatus.ShouldBe(HttpStatusCode.BadRequest);
        deep["data"].ShouldBeNull();
        deep["errors"]!.AsArray().Codes().ShouldBe(["FCMS1644"]);

        // A hundred entries, each with a relation to populate, weigh more than the limit of this host.
        var (costlyStatus, costly) = await client.SendAsync("{ articleList(pageSize: 100) { items { hero } } }");
        costlyStatus.ShouldBe(HttpStatusCode.BadRequest);
        costly["data"].ShouldBeNull();
        costly["errors"]!.AsArray().Codes().ShouldBe(["FCMS1645"]);
        (await client.QueryAsync("{ articleList(pageSize: 20) { items { hero } } }"))["articleList"]!["items"]!.AsArray().Count.ShouldBe(2);

        // The root field weighs 10, and each populated field 1 for every entry the page may hold; the others nothing.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = "{ articleList(pageSize: 20) { items { title slug hero } } }" }),
        };
        request.Headers.Add("GraphQL-Cost", "report");
        using var response = await client.SendAsync(request, Token);
        var report = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        report["extensions"]?["operationCost"]?["fieldCost"]?.GetValue<double>().ShouldBe(30, report.ToJsonString());
    }

    [Fact]
    public async Task ARequestOverTheSizeLimitIsRefusedAsRestRefusesOne()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");
        var query = "{ articleList { items { title } } } #" + new string('x', 5000);

        using var response = await client.PostAsync(
            new Uri(Endpoint, UriKind.Relative),
            new StringContent(new JsonObject { ["query"] = query }.ToJsonString(), Encoding.UTF8, "application/json"),
            Token);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>().ShouldBe("FCMS3111");
    }

    [Fact]
    public async Task ATypeAddedByASchemaContributorCanBeQueried()
    {
        using var anonymous = blog.Anonymous();

        var data = await anonymous.QueryAsync("""{ greeting(name: "Ada") { __typename text } }""");

        data["greeting"]!["__typename"]!.GetValue<string>().ShouldBe("Greeting");
        data["greeting"]!["text"]!.GetValue<string>().ShouldBe("Hello, Ada.");
    }

    [Fact]
    public async Task OnlyQueriesAreAnsweredToGetAndNoUploadOrBatchIsTaken()
    {
        using var client = await blog.KeyAsync(ApiKeyClass.Delivery, "content.*.read");

        using var get = await client.GetAsync(new Uri($"{Endpoint}?query={Uri.EscapeDataString("{ articleList { items { slug } } }")}", UriKind.Relative), Token);
        get.StatusCode.ShouldBe(HttpStatusCode.OK, await get.Content.ReadAsStringAsync(Token));
        JsonNode.Parse(await get.Content.ReadAsStringAsync(Token))!["data"]!["articleList"]!["items"].Strings("slug").ShouldContain(BlogSeeder.WelcomeSlug);

        using var mutation = await client.GetAsync(new Uri($"{Endpoint}?query={Uri.EscapeDataString("mutation { deleteAuthor(id: \"x\") }")}", UriKind.Relative), Token);
        mutation.IsSuccessStatusCode.ShouldBeFalse();

        using var batch = await client.PostAsync(
            new Uri(Endpoint, UriKind.Relative),
            new StringContent("""[{ "query": "{ __typename }" }, { "query": "{ __typename }" }]""", Encoding.UTF8, "application/json"),
            Token);
        batch.IsSuccessStatusCode.ShouldBeFalse();
    }
}
