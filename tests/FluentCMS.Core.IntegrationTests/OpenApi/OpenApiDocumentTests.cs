using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.Schema;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.OpenApi;

// A Development host with the Blog sample that maps the document and the UI as a host's Program.cs does.
public sealed class BlogOpenApiFixture : IAsyncLifetime
{
    private readonly CmsWebHost _host = new();

    public string Text { get; private set; } = "";

    public JsonObject Document { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var app = _host.Build(
            Environments.Development,
            configure: builder => builder.Services.AddSingleton(BlogSchema()),
            pipeline: app =>
            {
                app.UseFluentCms();
                app.MapGet("/mine", () => "the host's own endpoint");
                app.MapFluentCmsOpenApi();
            },
            cms: cms => cms.AddBlogSample());
        await app.StartAsync(TestContext.Current.CancellationToken);

        Client = app.GetTestClient();
        Text = await Client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        Document = JsonNode.Parse(Text)!.AsObject();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.DisposeAsync();
    }

    public static SchemaModel BlogSchema()
    {
        var builder = new SchemaBuilder();
        foreach (var type in BlogSample.ContentTypes)
        {
            builder.AddContentType(type);
        }

        return builder.Build();
    }
}

[Xunit.Collection(ProcessEnvironment.Collection)]
public sealed class OpenApiDocumentTests(BlogOpenApiFixture fixture) : IClassFixture<BlogOpenApiFixture>
{
    private JsonObject Document => fixture.Document;

    private JsonObject Schemas => Document["components"]!["schemas"]!.AsObject();

    private JsonObject Parameters => Document["components"]!["parameters"]!.AsObject();

    private JsonObject Paths => Document["paths"]!.AsObject();

    [Fact]
    public void TheDocumentIsValidOpenApi31ForTheBlogSample()
    {
        Document["openapi"]!.GetValue<string>().ShouldStartWith("3.1.");

        var read = new OpenApiJsonReader().Read(JsonNode.Parse(fixture.Text)!, new Uri("http://localhost/openapi/v1.json"), new OpenApiReaderSettings());

        read.Diagnostic!.Errors.ShouldBeEmpty();
        read.Diagnostic.SpecificationVersion.ShouldBe(OpenApiSpecVersion.OpenApi3_1);
        read.Document!.Validate(ValidationRuleSet.GetDefaultRuleSet())!.ShouldBeEmpty();
    }

    [Fact]
    public void EveryReferenceResolves()
    {
        var references = References(Document).Distinct().ToList();

        references.ShouldNotBeEmpty();
        foreach (var reference in references)
        {
            reference.ShouldStartWith("#/components/");
            var parts = reference["#/components/".Length..].Split('/');
            Document["components"]![parts[0]]?[parts[1]].ShouldNotBeNull($"{reference} does not resolve");
        }
    }

    [Fact]
    public void EachContentTypeComponentAndBlockHasItsOwnSchema()
    {
        foreach (var name in new[] { "entry-article", "entry-author", "entry-tag", "component-seo", "block-gallery", "block-hero", "block-quote" })
        {
            Schemas[name].ShouldNotBeNull(name);
        }

        var hero = Schemas["block-hero"]!;
        hero["properties"]!["type"]!["const"]!.GetValue<string>().ShouldBe("hero");
        hero["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["type"]);
        hero["properties"]!["heading"]!["type"]!.ToJsonString().ShouldBe("""["null","string"]""");

        var article = Schemas["entry-article"]!["properties"]!;
        article["id"]!["readOnly"]!.GetValue<bool>().ShouldBeTrue();
        article["title"]!["type"]!.GetValue<string>().ShouldBe("string");
        article["title"]!["maxLength"]!.GetValue<int>().ShouldBe(200);
        article["category"]!["enum"]!.AsArray().Select(item => item?.GetValue<string>()).ShouldBe(["news", "opinion", "video", null]);
        article["seo"]!.ToJsonString().ShouldContain("#/components/schemas/component-seo");
        article["author"]!.ToJsonString().ShouldContain("#/components/schemas/entry-author");
    }

    [Fact]
    public void ABlockFieldIsAOneOfWithATypeDiscriminator()
    {
        var sections = Schemas["entry-article"]!["properties"]!["sections"]!;

        sections["type"]!.GetValue<string>().ShouldBe("array");
        var items = sections["items"]!;
        items["oneOf"]!.AsArray().Select(item => item!["$ref"]!.GetValue<string>())
            .ShouldBe(["#/components/schemas/block-hero", "#/components/schemas/block-quote", "#/components/schemas/block-gallery"]);
        items["discriminator"]!["propertyName"]!.GetValue<string>().ShouldBe("type");
        items["discriminator"]!["mapping"]!["quote"]!.GetValue<string>().ShouldBe("#/components/schemas/block-quote");
    }

    [Fact]
    public void BothSecuritySchemesArePresent()
    {
        var schemes = Document["components"]!["securitySchemes"]!;

        var code = schemes["oauth2"]!["flows"]!["authorizationCode"]!;
        schemes["oauth2"]!["type"]!.GetValue<string>().ShouldBe("oauth2");
        code["authorizationUrl"]!.GetValue<string>().ShouldEndWith("/connect/authorize");
        code["tokenUrl"]!.GetValue<string>().ShouldEndWith("/connect/token");
        code["x-usePkce"]!.GetValue<string>().ShouldBe("SHA-256");
        code["scopes"]!.AsObject().Select(scope => scope.Key).ShouldBe(["openid", "profile", "email", "offline_access", "draft", "management"]);

        schemes["apiKey"]!["type"]!.GetValue<string>().ShouldBe("apiKey");
        schemes["apiKey"]!["in"]!.GetValue<string>().ShouldBe("header");
        schemes["apiKey"]!["name"]!.GetValue<string>().ShouldBe("X-Api-Key");

        Paths["/api/management/v1/content/article"]!["get"]!["security"]!.ToJsonString()
            .ShouldBe("""[{"oauth2":["management"]},{"apiKey":[]}]""");
        Paths["/api/management/v1/users"]!["get"]!["security"]!.ToJsonString()
            .ShouldBe("""[{"oauth2":["management"]},{"apiKey":[]}]""");
        Paths["/api/delivery/v1/article"]!["get"]!["security"]!.ToJsonString()
            .ShouldBe("""[{},{"oauth2":[]},{"apiKey":[]}]""");
    }

    [Fact]
    public void TheFilterParametersAreDocumented()
    {
        var filter = Parameters["filter-article"]!;
        filter["name"]!.GetValue<string>().ShouldBe("filter");
        filter["in"]!.GetValue<string>().ShouldBe("query");
        filter["style"]!.GetValue<string>().ShouldBe("deepObject");
        filter["description"]!.GetValue<string>().ShouldContain("filter[field][operator]=value");

        var fields = filter["schema"]!["properties"]!;
        fields["title"]!["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["eq", "ne", "in", "nin", "contains", "startsWith", "null", "notNull"]);
        fields["sections"]!["properties"]!["hasBlock"]!["enum"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["hero", "quote", "gallery"]);
        fields["publishedOn"]!["properties"]!["gt"]!["format"]!.GetValue<string>().ShouldBe("date-time");
        fields["seo.metaTitle"]!["properties"]!["eq"]!["type"]!.GetValue<string>().ShouldBe("string");
        fields["createdAt"].ShouldNotBeNull();
        fields["body"]!["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["null", "notNull"]);

        Parameters["sort-article"]!["description"]!.GetValue<string>().ShouldContain("publishedOn");

        var list = Paths["/api/delivery/v1/article"]!["get"]!["parameters"]!.AsArray().Select(item => item!["$ref"]!.GetValue<string>()).ToList();
        list.ShouldContain("#/components/parameters/filter-article");
        list.ShouldContain("#/components/parameters/sort-article");
        list.ShouldContain("#/components/parameters/populate");
        list.ShouldContain("#/components/parameters/cursor");

        var one = Paths["/api/delivery/v1/article/{id}"]!["get"]!["parameters"]!.AsArray().Select(item => item!["$ref"]?.GetValue<string>()).ToList();
        one.ShouldNotContain("#/components/parameters/filter-article");
        one.ShouldContain("#/components/parameters/fields");
    }

    [Fact]
    public void ContentRoutesAreWrittenOncePerType()
    {
        Paths.Select(path => path.Key).ShouldNotContain(path => path.StartsWith("/api/delivery/v1/{type}", StringComparison.Ordinal) || path.StartsWith("/api/management/v1/content/{type}", StringComparison.Ordinal));

        foreach (var type in new[] { "article", "author", "tag" })
        {
            Paths[$"/api/delivery/v1/{type}"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["properties"]!["items"]!["items"]!["$ref"]!
                .GetValue<string>().ShouldBe($"#/components/schemas/entry-{type}");
            Paths[$"/api/management/v1/content/{type}/{{id}}"]!.AsObject().Select(item => item.Key).ShouldBe(["get", "put", "patch", "delete"], ignoreOrder: true);
        }

        var create = Paths["/api/management/v1/content/article"]!["post"]!;
        create["responses"]!["201"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/entry-article");
        create["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/entry-article");
        create["responses"]!["default"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/responses/problem");

        Paths["/api/management/v1/content/article/{id}"]!["put"]!["parameters"]!.ToJsonString().ShouldContain("#/components/parameters/if-match\"");
        Paths["/api/delivery/v1/tag/by/{field}/{value}"]!["get"]!["parameters"]![0]!["schema"]!["enum"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ShouldBe(["slug"]);

        var operationIds = Paths.SelectMany(path => path.Value!.AsObject().Select(operation => operation.Value!["operationId"]!.GetValue<string>())).ToList();
        operationIds.ShouldBeUnique();
        operationIds.ShouldContain("deliveryListArticle");
        operationIds.ShouldContain("publishArticle");
        operationIds.ShouldContain("getUsersById");
    }

    [Fact]
    public void TheDocumentHoldsTheDeliveryAndManagementRoutesOnly()
    {
        var paths = Paths.Select(path => path.Key).ToList();

        paths.ShouldContain("/api/management/v1/schema");
        paths.ShouldAllBe(path => path.StartsWith("/api/delivery/v1/", StringComparison.Ordinal) || path.StartsWith("/api/management/v1/", StringComparison.Ordinal));
        Document["tags"]!.AsArray().Select(tag => tag!["name"]!.GetValue<string>()).ShouldContain("Content: article");
    }

    [Fact]
    public void TheExamplesComeFromTheBlogSample()
    {
        var article = Schemas["entry-article"]!;
        var example = article["examples"]![0]!.AsObject();

        example["title"]!.GetValue<string>().ShouldBe("Welcome to FluentCMS");
        example["slug"]!.GetValue<string>().ShouldBe(BlogSeeder.WelcomeSlug);
        foreach (var (name, schema) in new[] { ("entry-article", "article"), ("entry-author", "author"), ("entry-tag", "tag"), ("component-seo", "seo"), ("block-hero", "hero"), ("block-quote", "quote"), ("block-gallery", "gallery") })
        {
            var properties = Schemas[name]!["properties"]!.AsObject();
            var sample = Schemas[name]!["examples"]![0]!.AsObject();
            sample.Select(item => item.Key).ShouldAllBe(key => properties.ContainsKey(key), $"the {schema} example has a key its schema lacks");
        }
    }

    [Fact]
    public async Task ScalarServesTheUi()
    {
        var token = TestContext.Current.CancellationToken;
        using var redirect = await fixture.Client.GetAsync(new Uri("/scalar", UriKind.Relative), token);
        using var response = await fixture.Client.GetAsync(new Uri("/scalar/", UriKind.Relative), token);
        using var script = await fixture.Client.GetAsync(new Uri("/scalar/scalar.js", UriKind.Relative), token);
        var html = await response.Content.ReadAsStringAsync(token);

        redirect.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        redirect.Headers.Location!.OriginalString.ShouldBe("scalar/");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("\"url\":\"openapi/v1.json\"");
        html.ShouldContain("FluentCMS API");
    }

    private static IEnumerable<string> References(JsonNode? node) => node switch
    {
        JsonObject json => json.SelectMany(item => item.Key == "$ref" && item.Value is JsonValue value ? [value.GetValue<string>()] : References(item.Value)),
        JsonArray array => array.SelectMany(References),
        _ => [],
    };
}
