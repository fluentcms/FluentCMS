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
            configure: builder => builder.Services.AddSingleton(services => BlogSchema(services.GetRequiredService<IFieldTypeRegistry>())),
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

    // With the registry of the container, which knows the media field type of the plugin.
    public static SchemaModel BlogSchema(IFieldTypeRegistry fieldTypes)
    {
        var builder = new SchemaBuilder(fieldTypes);
        foreach (var type in BlogSample.ContentTypes)
        {
            builder.AddContentType(type);
        }

        return builder.Build();
    }
}

public sealed class OpenApiDocumentTests(BlogOpenApiFixture fixture) : IClassFixture<BlogOpenApiFixture>
{
    private const string Management = "/api/management/v1";

    private const string SettingsIfMatch = "settings-if-match";

    private static readonly string[] AdminGroups = ["users", "invitations", "roles", "permissions", "api-keys", "sites", "settings", "jobs", "events", "activity", "me", "plugins", "system"];

    private static readonly Dictionary<string, string[]> ReadsQuery = new(StringComparer.Ordinal)
    {
        ["get /users"] = ["search", "page", "pageSize"],
        ["get /jobs"] = ["status", "type", "queue", "page", "pageSize"],
        ["get /jobs/dead-letter"] = ["page", "pageSize"],
        ["get /events/dead-letter"] = ["page", "pageSize"],
        ["delete /events/dead-letter"] = ["handler"],
        ["get /activity"] = ["actorId", "actorKind", "origin", "action", "targetType", "targetId", "from", "to", "page", "pageSize"],
    };

    private static readonly string[] TakesABody =
    [
        "patch /users/{id}", "post /invitations", "post /roles", "put /roles/{id}", "post /api-keys", "post /api-keys/{id}/expire",
        "post /api-keys/{id}/rotate", "put /api-keys/{id}/allowed-addresses",
        "patch /sites/{id}", "post /sites/{id}/locales", "patch /sites/{id}/locales/{code}", "put /settings/{area}", "put /settings/{area}/defaults",
        "put /plugins/{name}/sites/{site}",
        "post /me/two-factor/verify", "post /me/two-factor/recovery-codes", "post /me/two-factor/disable", "put /me/preferences",
    ];

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
    public void ATypeOfTheSchemaRoutesDescribesItsDisplayNameAndTitleField()
    {
        var type = Response(Paths[$"{Management}/schema/types/{{type}}"]!["get"]!, "200");

        foreach (var name in new[] { "displayName", "titleField" })
        {
            type["properties"]![name]!["type"]!.ToJsonString().ShouldBe("""["null","string"]""", name);
            type["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldNotContain(name);
        }
    }

    [Fact]
    public void TheTransferEndpointsAnswerAPackageAndLinesOfJson()
    {
        Paths[$"{Management}/transfer/export"]!["post"]!["responses"]!["200"]!["content"]!["application/zip"].ShouldNotBeNull();
        var import = Paths[$"{Management}/transfer/import"]!["post"]!;
        import["responses"]!["200"]!["content"]!["application/x-ndjson"].ShouldNotBeNull();
        QueryParameters(import).Select(parameter => parameter["name"]!.GetValue<string>())
            .ShouldBe(["site", "locale", "dryRun", "overwrite", "missingLocales", "authors", "webhooks", "roles", "types", "options"], ignoreOrder: true);
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
        foreach (var name in new[] { "entry-article", "input-article", "entry-author", "entry-tag", "component-seo", "block-gallery", "block-hero", "block-quote" })
        {
            Schemas[name].ShouldNotBeNull(name);
        }

        var hero = Schemas["block-hero"]!;
        hero["properties"]!["type"]!["const"]!.GetValue<string>().ShouldBe("hero");
        hero["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["type"]);
        hero["properties"]!.AsObject().Select(pair => pair.Key).Take(2).ShouldBe(["type", "id"]);
        hero["properties"]!["id"]!["pattern"]!.GetValue<string>().ShouldBe("^[A-Za-z0-9_-]{1,64}$");
        hero["properties"]!["heading"]!["type"]!.ToJsonString().ShouldBe("""["null","string"]""");

        var article = Schemas["entry-article"]!["properties"]!;
        article["id"]!["readOnly"]!.GetValue<bool>().ShouldBeTrue();
        article["title"]!["type"]!.GetValue<string>().ShouldBe("string");
        article["title"]!["maxLength"]!.GetValue<int>().ShouldBe(200);
        article["category"]!["enum"]!.AsArray().Select(item => item?.GetValue<string>()).ShouldBe(["news", "opinion", "video", null]);
        article["seo"]!.ToJsonString().ShouldContain("#/components/schemas/component-seo");
        article["author"]!.ToJsonString().ShouldContain("#/components/schemas/entry-author");

        foreach (var name in new[] { "scheduledPublishAt", "scheduledUnpublishAt" })
        {
            article[name]!["readOnly"]!.GetValue<bool>().ShouldBeTrue();
            article[name]!["type"]!.ToJsonString().ShouldBe("""["null","string"]""");
            article[name]!["format"]!.GetValue<string>().ShouldBe("date-time");
            article[name]!["description"]!.GetValue<string>().ShouldContain("Management API only");
        }

        var input = Schemas["input-article"]!["properties"]!.AsObject();
        input.ContainsKey("id").ShouldBeFalse();
        input.ContainsKey("scheduledPublishAt").ShouldBeFalse();
        input["author"]!.ToJsonString().ShouldBe("""{"type":["null","string"],"format":"uuid"}""");
        input["body"]!["anyOf"].ShouldBeNull();
    }

    // Every reserved name is a member a response can carry, so an entry describes each, and a new one fails here until it does.
    [Fact]
    public void AnEntryDescribesEveryReservedNameAsReadOnly()
    {
        var entry = Schemas["entry-article"]!["properties"]!.AsObject();
        var input = Schemas["input-article"]!["properties"]!.AsObject();

        foreach (var name in ContentItem.ReservedFieldNames)
        {
            entry[name].ShouldNotBeNull($"entry-article does not describe {name}");
            entry[name]!["readOnly"]!.GetValue<bool>().ShouldBeTrue(name);
            input.ContainsKey(name).ShouldBeFalse(name);
        }
    }

    [Fact]
    public void AnEntryDescribesTheTrashMarks()
    {
        var article = Schemas["entry-article"]!["properties"]!;

        article["deletedAt"]!["type"]!.GetValue<string>().ShouldBe("string");
        article["deletedAt"]!["format"]!.GetValue<string>().ShouldBe("date-time");
        article["deletedBy"]!["type"]!.GetValue<string>().ShouldBe("string");
        article["deletedBy"]!["format"]!.GetValue<string>().ShouldBe("uuid");
        foreach (var name in new[] { "deletedAt", "deletedBy" })
        {
            article[name]!["description"]!.GetValue<string>().ShouldContain("Only on a read of the trash");
        }

        var items = Response(Paths[$"{Management}/content/article/trash"]!["get"]!, "200")["properties"]!["items"]!["items"]!;
        items["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/entry-article");
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

        schemes["previewToken"]!["type"]!.GetValue<string>().ShouldBe("http");
        schemes["previewToken"]!["scheme"]!.GetValue<string>().ShouldBe("Preview");
        Paths["/api/delivery/v1/article/{id}"]!["get"]!["security"]!.ToJsonString()
            .ShouldBe("""[{},{"oauth2":[]},{"apiKey":[]},{"previewToken":[]}]""");
    }

    [Fact]
    public void ThePreviewTokenRouteHasItsBodies()
    {
        var mint = Paths["/api/management/v1/content/article/{id}/preview-token"]!["post"]!;

        mint["operationId"]!.GetValue<string>().ShouldBe("previewTokenArticle");
        (mint["requestBody"]!["required"]?.GetValue<bool>() ?? false).ShouldBeFalse();
        mint["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/preview-token-request");
        mint["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/preview-token");

        var schemas = Document["components"]!["schemas"]!;
        schemas["preview-token-request"]!["properties"]!.AsObject().Select(property => property.Key).ShouldBe(["minutes", "locale"]);
        schemas["preview-token"]!["required"]!.ToJsonString().ShouldBe("""["token","scheme","expiresAt","expiresIn","type","id","site"]""");
        var lifetime = schemas["preview-token"]!["properties"]!["expiresIn"]!;
        (lifetime["type"]!.GetValue<string>(), lifetime["format"]!.GetValue<string>()).ShouldBe(("integer", "int32"));
        foreach (var url in new[] { "url", "publishedUrl" })
        {
            var property = schemas["preview-token"]!["properties"]![url]!;
            (property["type"]!.ToJsonString(), property["format"]!.GetValue<string>()).ShouldBe(("""["null","string"]""", "uri"));
        }
    }

    [Fact]
    public void ATypeOfTheSchemaSaysWhetherItIsPreviewable()
    {
        var types = Resolve(Response(Paths[$"{Management}/schema"]!["get"]!, "200"))["properties"]!["types"]!["items"]!;

        Resolve(types)["properties"]!["previewable"]!["type"]!.GetValue<string>().ShouldBe("boolean");
        Resolve(Response(Paths[$"{Management}/schema/types/{{type}}"]!["get"]!, "200"))["properties"]!["previewable"].ShouldNotBeNull();
    }

    [Fact]
    public void AVersionNamesTheKindAndTheNameOfItsAuthor()
    {
        var item = Schemas["versions"]!["properties"]!["items"]!["items"]!["properties"]!;

        item.AsObject().Select(property => property.Key).ShouldBe(["number", "locale", "status", "authorId", "authorKind", "authorName", "origin", "clientId", "message", "createdAt"]);
        item["authorKind"]!["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ShouldBe(["adminUser", "member", "apiKey", "serviceAccount", "system", "anonymous"]);
        item["authorName"]!["type"]!.GetValue<string>().ShouldBe("string");
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
        list.ShouldContain("#/components/parameters/q");

        var one = Paths["/api/delivery/v1/article/{id}"]!["get"]!["parameters"]!.AsArray().Select(item => item!["$ref"]?.GetValue<string>()).ToList();
        one.ShouldNotContain("#/components/parameters/filter-article");
        one.ShouldNotContain("#/components/parameters/q");
        one.ShouldContain("#/components/parameters/fields");
    }

    // A member a client may ignore: optional, and said only of a search the index cut (#3302).
    [Fact]
    public void AContentListSaysWhenItsSearchWasCut()
    {
        foreach (var path in new[] { "/api/delivery/v1/article", $"{Management}/content/article" })
        {
            var list = Response(Paths[path]!["get"]!, "200");
            var truncated = list["properties"]!["searchTruncated"]!;

            truncated["type"]!.GetValue<string>().ShouldBe("boolean", path);
            truncated["description"]!.GetValue<string>().ShouldContain("MaxSearchResults");
            truncated["description"]!.GetValue<string>().ShouldContain("never sent on the trash list");
            list["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["items", "pageSize"]);
        }

        Parameters["q"]!["description"]!.GetValue<string>().ShouldContain("searchTruncated");
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
        create["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/input-article");
        create["responses"]!["201"]!["headers"]!["ETag"].ShouldNotBeNull();
        Paths["/api/management/v1/content/article/{id}/versions/{number}"]!["get"]!["responses"]!["200"]!["headers"].ShouldBeNull();
        create["responses"]!["default"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/responses/problem");

        Paths["/api/management/v1/content/article/{id}"]!["put"]!["parameters"]!.ToJsonString().ShouldContain("#/components/parameters/if-match\"");
        create["parameters"]!.ToJsonString().ShouldContain("#/components/parameters/idempotency-key\"");
        create["responses"]!["201"]!["headers"]!["Idempotent-Replayed"].ShouldNotBeNull();
        var bulk = Paths["/api/management/v1/content/article/bulk"]!["post"]!;
        bulk["parameters"]!.ToJsonString().ShouldContain("#/components/parameters/idempotency-key\"");
        bulk["responses"]!["200"]!["headers"]!["Idempotent-Replayed"].ShouldNotBeNull();
        Paths["/api/management/v1/content/article/{id}"]!["put"]!["parameters"]!.ToJsonString().ShouldNotContain("idempotency-key");
        var key = Parameters["idempotency-key"]!;
        key["name"]!.GetValue<string>().ShouldBe("Idempotency-Key");
        key["in"]!.GetValue<string>().ShouldBe("header");
        (key["required"]?.GetValue<bool>() ?? false).ShouldBeFalse();
        key["schema"]!["maxLength"]!.GetValue<int>().ShouldBe(255);
        Paths["/api/delivery/v1/tag/by/{field}/{value}"]!["get"]!["parameters"]![0]!["schema"]!["enum"]!.AsArray()
            .Select(item => item!.GetValue<string>()).ShouldBe(["slug"]);

        var operationIds = Paths.SelectMany(path => path.Value!.AsObject().Select(operation => operation.Value!["operationId"]!.GetValue<string>())).ToList();
        operationIds.ShouldBeUnique();
        operationIds.ShouldContain("deliveryListArticle");
        operationIds.ShouldContain("publishArticle");
        operationIds.ShouldContain("getUsersById");

        // The media plugin, which the Blog sample brings, has /media/{id} in both groups.
        operationIds.ShouldContain("deliveryGetMediaById");
        operationIds.ShouldContain("getMediaById");
    }

    // A Delivery read also answers HEAD, which HTTP gives every GET, so the document describes the GET alone.
    [Fact]
    public void ADeliveryReadIsDescribedAsAGetOnly()
    {
        Paths.Where(path => path.Key.StartsWith("/api/delivery/v1/", StringComparison.Ordinal))
            .SelectMany(path => path.Value!.AsObject().Select(operation => operation.Key))
            .Distinct()
            .ShouldBe(["get"]);
    }

    [Fact]
    public void RoutesBesideContentComeFromTheirEndpoints()
    {
        var read = Paths["/api/management/v1/me/two-factor"]!["get"]!;
        var verify = Paths["/api/management/v1/me/two-factor/verify"]!["post"]!;

        read["operationId"]!.GetValue<string>().ShouldBe("getMeTwoFactor");
        read["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()).ShouldBe(["Me"]);
        verify["operationId"]!.GetValue<string>().ShouldBe("postMeTwoFactorVerify");
        verify["security"]!.ToJsonString().ShouldBe("""[{"oauth2":["management"]},{"apiKey":[]}]""");
        foreach (var action in new[] { "enrol", "recovery-codes", "disable" })
        {
            Paths[$"/api/management/v1/me/two-factor/{action}"]!["post"].ShouldNotBeNull(action);
        }
    }

    [Fact]
    public void EveryRouteOfTheGroupsBesideContentDescribesItsBodies()
    {
        var operations = Paths
            .Where(path => path.Key.StartsWith(Management, StringComparison.Ordinal) && AdminGroups.Contains(path.Key[Management.Length..].Split('/')[1]))
            .SelectMany(path => path.Value!.AsObject().Select(operation => (Name: $"{operation.Key} {path.Key[Management.Length..]}", Operation: operation.Value!)))
            .ToList();

        operations.Select(operation => operation.Name.Split(' ')[1].Split('/')[1]).Distinct().ShouldBe(AdminGroups, ignoreOrder: true);
        foreach (var (name, operation) in operations)
        {
            var responses = operation["responses"]!.AsObject();
            var success = responses.Where(response => response.Key.StartsWith('2')).ShouldHaveSingleItem(name);
            if (success.Key != "204")
            {
                success.Value!["content"]!["application/json"]!["schema"].ShouldNotBeNull(name);
            }

            responses["default"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/responses/problem", name);
            QueryParameters(operation).Select(parameter => parameter["name"]!.GetValue<string>()).ShouldBe(["site", "locale", .. ReadsQuery.GetValueOrDefault(name, [])], name);
            if (TakesABody.Contains(name))
            {
                var body = Resolve(operation["requestBody"]!["content"]!["application/json"]!["schema"]!);
                if (!name.Contains("/settings/", StringComparison.Ordinal))
                {
                    body["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(name);
                }
            }
            else
            {
                operation["requestBody"].ShouldBeNull(name);
            }
        }

        TakesABody.ShouldAllBe(name => operations.Any(operation => operation.Name == name));
        ReadsQuery.Keys.ShouldAllBe(name => operations.Any(operation => operation.Name == name));
    }

    [Fact]
    public void TheActivityAndJobListsDescribeTheirFiltersAndPaging()
    {
        var activity = QueryParameters(Paths[$"{Management}/activity"]!["get"]!).ToDictionary(parameter => parameter["name"]!.GetValue<string>());

        activity["site"]!["description"]!.GetValue<string>().ShouldContain("X-Cms-Site");
        activity["locale"]!["description"]!.GetValue<string>().ShouldContain("reads nothing by it");
        Schema(activity["actorId"]!).ShouldBe("""{"format":"uuid","type":"string"}""");
        Values(activity["actorKind"]!).ShouldBe(["adminUser", "member", "apiKey", "serviceAccount", "system", "anonymous"]);
        Values(activity["origin"]!).ShouldBe(["admin", "api", "mcp", "job", "cli", "seed", "import"]);
        Schema(activity["action"]!).ShouldBe("""{"minLength":1,"type":"string"}""");
        activity["action"]!["description"]!.GetValue<string>().ShouldContain("trailing *");
        foreach (var name in new[] { "from", "to" })
        {
            activity[name]!["schema"]!["format"]!.GetValue<string>().ShouldBe("date-time", name);
        }

        activity.Values.ShouldAllBe(parameter => parameter["in"]!.GetValue<string>() == "query" && parameter["required"] == null);
        Schema(activity["page"]!).ShouldBe("""{"default":1,"format":"int32","minimum":1,"type":"integer"}""");
        Schema(activity["pageSize"]!).ShouldBe("""{"default":50,"format":"int32","maximum":100,"minimum":1,"type":"integer"}""");

        var jobs = QueryParameters(Paths[$"{Management}/jobs"]!["get"]!).ToDictionary(parameter => parameter["name"]!.GetValue<string>());
        Values(jobs["status"]!).ShouldBe(["pending", "running", "succeeded", "dead_lettered"]);
        jobs["type"]!["schema"]!["type"]!.GetValue<string>().ShouldBe("string");
        jobs["queue"]!["schema"]!["type"]!.GetValue<string>().ShouldBe("string");
        jobs["pageSize"]!["schema"]!["maximum"]!.GetValue<int>().ShouldBe(100);
        QueryParameters(Paths[$"{Management}/users"]!["get"]!).Select(parameter => parameter["name"]!.GetValue<string>()).ShouldContain("search");
    }

    [Fact]
    public void TheSystemRoutesDescribeTheirReports()
    {
        var health = Response(Paths[$"{Management}/system/health"]!["get"]!, "200");
        health["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["status", "totalDurationMs", "checks"]);
        health["properties"]!["status"]!["description"]!.GetValue<string>().ShouldContain("Degraded");
        health["properties"]!["totalDurationMs"]!["type"]!.GetValue<string>().ShouldBe("number");
        var check = Resolve(health["properties"]!["checks"]!["items"]!);
        check["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["name", "status", "durationMs", "tags", "description", "error"]);
        check["properties"]!["tags"]!["items"]!["type"]!.GetValue<string>().ShouldBe("string");
        check["properties"]!["error"]!["type"]!.ToJsonString().ShouldBe("""["null","string"]""");

        var diagnostics = Response(Paths[$"{Management}/system/diagnostics"]!["get"]!, "200");
        diagnostics["properties"]!.AsObject().Select(item => item.Key)
            .ShouldBe(["startedAt", "environment", "version", "failure", "dataProvider", "assemblies", "plugins", "providerSlots", "pendingMigrations", "schema", "insecureDefaults"]);
        Resolve(diagnostics["properties"]!["schema"]!)["properties"]!["drifted"]!["type"]!.GetValue<string>().ShouldBe("boolean");
    }

    [Fact]
    public void TheTwoFactorRoutesHaveTheirSchemas()
    {
        var status = Response(Paths[$"{Management}/me/two-factor"]!["get"]!, "200");
        status["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["enabled", "required", "pending", "recoveryCodesLeft"]);
        status["properties"]!["enabled"]!["type"]!.GetValue<string>().ShouldBe("boolean");
        status["properties"]!["recoveryCodesLeft"]!["type"]!.GetValue<string>().ShouldBe("integer");

        var enrol = Response(Paths[$"{Management}/me/two-factor/enrol"]!["post"]!, "200");
        enrol["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["sharedKey", "authenticatorUri"]);

        var verify = Paths[$"{Management}/me/two-factor/verify"]!["post"]!;
        verify["requestBody"]!["required"]!.GetValue<bool>().ShouldBeTrue();
        var code = Resolve(verify["requestBody"]!["content"]!["application/json"]!["schema"]!);
        code["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["code"]);
        code["properties"]!["code"]!["type"]!.GetValue<string>().ShouldBe("string");
        Response(verify, "200")["properties"]!["recoveryCodes"]!["items"]!["type"]!.GetValue<string>().ShouldBe("string");

        var disable = Paths[$"{Management}/me/two-factor/disable"]!["post"]!;
        disable["responses"]!["204"]!["content"].ShouldBeNull();
        disable["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/TwoFactorCodeRequest");
    }

    // The row rules a grant may name, beside the catalogue (#3417): names and types, nothing of what a rule compares.
    [Fact]
    public void TheRowRulesHaveTheirSchema()
    {
        var operation = Paths[$"{Management}/permissions/row-rules"]!["get"]!;
        operation["operationId"]!.GetValue<string>().ShouldBe("getPermissionsRowRules");

        var rule = Resolve(Response(operation, "200")["properties"]!["items"]!["items"]!);
        rule["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["name", "typeNames"]);
        rule["properties"]!["name"]!["type"]!.GetValue<string>().ShouldBe("string");
        var types = rule["properties"]!["typeNames"]!;
        types["type"]!.ToJsonString().ShouldBe("""["null","array"]""");
        types["items"]!["type"]!.GetValue<string>().ShouldBe("string");
        types["description"]!.GetValue<string>().ShouldContain("every type");
    }

    // What the admin draws itself from (#3096): the caller's access, the locales of its sites and its preferences.
    [Fact]
    public void TheAccessLocalesAndPreferencesOfTheCallerHaveTheirSchemas()
    {
        var access = Response(Paths[$"{Management}/me/access"]!["get"]!, "200");
        access["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["site", "locale", "permissions", "instancePermissions", "limitedPermissions", "search", "types"]);
        access["properties"]!["permissions"]!["items"]!["type"]!.GetValue<string>().ShouldBe("string");
        var type = Resolve(access["properties"]!["types"]!["additionalProperties"]!);
        type["properties"]!.AsObject().Select(item => item.Key)
            .ShouldBe(["drafts", "create", "update", "delete", "publish", "unpublish", "creatableFields", "updatableFields", "conditional"]);

        var site = Resolve(Response(Paths[$"{Management}/me/sites"]!["get"]!, "200")["properties"]!["items"]!["items"]!);
        var locale = Resolve(site["properties"]!["locales"]!["items"]!);
        locale["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["code", "position", "enabled"]);

        var read = Response(Paths[$"{Management}/me/preferences"]!["get"]!, "200");
        read["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["culture", "timeZone", "dateFormat", "numberFormat"]);
        var write = Paths[$"{Management}/me/preferences"]!["put"]!;
        Resolve(write["requestBody"]!["content"]!["application/json"]!["schema"]!)["properties"]!.AsObject().Select(item => item.Key)
            .ShouldBe(["culture", "timeZone", "dateFormat", "numberFormat"]);
        Response(write, "200")["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["culture", "timeZone", "dateFormat", "numberFormat"]);
    }

    // A locale another one names as a fallback is not removed, and no chain changes with a removal (#3517).
    [Fact]
    public void TheRemovalOfALocaleSaysWhatRefusesIt()
    {
        var removal = Paths[$"{Management}/sites/{{id}}/locales/{{code}}"]!["delete"]!;

        removal["responses"]!["204"].ShouldNotBeNull();
        var description = removal["description"]!.GetValue<string>();
        description.ShouldContain("FCMS3136");
        description.ShouldContain("FCMS3143");
        description.ShouldContain("locales.{code}.fallbacks");
    }

    [Fact]
    public void ASettingsReadRefersToTheJsonSchemaOfItsArea()
    {
        var read = Response(Paths[$"{Management}/settings/{{area}}"]!["get"]!, "200");
        var properties = read["properties"]!;

        read["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["area", "siteId", "source", "version", "value", "secretsSet", "secretsInherited", "schema"]);
        Resolve(properties["source"]!)["enum"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["code", "default", "site"]);
        properties["version"]!["type"]!.GetValue<string>().ShouldBe("integer");
        properties["siteId"]!["type"]!.ToJsonString().ShouldBe("""["null","string"]""");
        Resolve(properties["value"]!)["type"]!.GetValue<string>().ShouldBe("object");
        properties["value"]!["description"]!.GetValue<string>().ShouldContain("JSON schema under schema");
        properties["secretsInherited"]!["description"]!.GetValue<string>().ShouldContain("read from the instance defaults");
        Resolve(properties["schema"]!)["type"]!.GetValue<string>().ShouldBe("object");
        properties["schema"]!["description"]!.GetValue<string>().ShouldContain("JSON schema of the area's settings");

        var write = Paths[$"{Management}/settings/{{area}}/defaults"]!["put"]!;
        write["requestBody"]!["description"]!.GetValue<string>().ShouldContain("JSON schema of the read");
        Resolve(write["requestBody"]!["content"]!["application/json"]!["schema"]!)["type"]!.GetValue<string>().ShouldBe("object");
        write["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/SettingResponse");
    }

    // The media plugin, which the Blog sample brings, declares the bodies of its reads with the URLs of each asset (#3100).
    [Fact]
    public void TheMediaReadsDescribeTheUrlsOfEachAsset()
    {
        var read = Response(Paths[$"{Management}/media/{{id}}"]!["get"]!, "200");
        var list = Response(Paths[$"{Management}/media"]!["get"]!, "200");
        var urls = Resolve(read["properties"]!["urls"]!);

        read["properties"]!.AsObject().Select(item => item.Key).ShouldContain("checksum");
        Resolve(list["properties"]!["items"]!["items"]!)["properties"]!["urls"]!.ToJsonString().ShouldBe(read["properties"]!["urls"]!.ToJsonString());
        list["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["items", "total", "page", "pageSize"]);
        urls["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["original", "thumbnail", "expiresAt"]);
        urls["properties"]!["thumbnail"]!["description"]!.GetValue<string>().ShouldContain("320 by 320");
        urls["properties"]!["expiresAt"]!["description"]!.GetValue<string>().ShouldContain("downloadUrlSeconds");
    }

    [Fact]
    public void TheListOfSettingsAreasAndTheResetsAreDocumented()
    {
        var list = Response(Paths[$"{Management}/settings"]!["get"]!, "200");
        var area = Resolve(list["properties"]!["items"]!["items"]!);
        area["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["area", "plugin", "site", "defaults"]);
        area["properties"]!["plugin"]!["description"]!.GetValue<string>().ShouldContain("null for an area of core");
        area["properties"]!["site"]!["description"]!.GetValue<string>().ShouldContain("/settings/{area}");
        area["properties"]!["defaults"]!["description"]!.GetValue<string>().ShouldContain("/settings/{area}/defaults");

        foreach (var path in new[] { $"{Management}/settings/{{area}}", $"{Management}/settings/{{area}}/defaults" })
        {
            var reset = Paths[path]!["delete"]!;
            reset["operationId"]!.GetValue<string>().ShouldStartWith("deleteSettingsByArea");
            reset["responses"]!["204"].ShouldNotBeNull(path);
            reset["requestBody"].ShouldBeNull(path);
        }
    }

    // A settings write and a reset need the version they read, "0" when nothing is stored; the read needs none.
    [Fact]
    public void TheSettingsWritesAndResetsRequireIfMatch()
    {
        var header = Parameters[SettingsIfMatch]!;
        header["name"]!.GetValue<string>().ShouldBe("If-Match");
        header["in"]!.GetValue<string>().ShouldBe("header");
        header["required"]!.GetValue<bool>().ShouldBeTrue();
        header["description"]!.GetValue<string>().ShouldContain("\"0\"");

        foreach (var path in new[] { $"{Management}/settings/{{area}}", $"{Management}/settings/{{area}}/defaults" })
        {
            foreach (var method in new[] { "put", "delete" })
            {
                Paths[path]![method]!["parameters"]!.ToJsonString().ShouldContain($"#/components/parameters/{SettingsIfMatch}\"", Case.Sensitive, $"{method} {path}");
            }

            Paths[path]!["get"]!["parameters"]!.ToJsonString().ShouldNotContain("if-match");
        }
    }

    [Fact]
    public void TheIdentityAndJobRoutesHaveTheirSchemas()
    {
        var users = Response(Paths[$"{Management}/users"]!["get"]!, "200");
        users["properties"]!["items"]!["items"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/UserInfo");
        users["properties"]!["total"]!["format"]!.GetValue<string>().ShouldBe("int64");

        var create = Paths[$"{Management}/roles"]!["post"]!;
        var role = Resolve(create["requestBody"]!["content"]!["application/json"]!["schema"]!);
        role["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["name", "grants"]);
        role["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["name", "description", "siteId", "requireTwoFactor", "grants"]);
        Resolve(Paths[$"{Management}/roles/{{id}}"]!["put"]!["requestBody"]!["content"]!["application/json"]!["schema"]!)["properties"]!.AsObject().ContainsKey("siteId").ShouldBeFalse();
        var grant = Resolve(role["properties"]!["grants"]!["items"]!);
        grant["properties"]!["constraints"]!.ToJsonString().ShouldContain("#/components/schemas/GrantConstraints");
        Schemas["GrantConstraints"]!["properties"]!.AsObject().Select(item => item.Key).ShouldBe(["sites", "locales", "fields", "rowRule", "resources"]);
        Response(create, "201")["properties"]!["builtIn"]!["type"]!.GetValue<string>().ShouldBe("boolean");

        var key = Response(Paths[$"{Management}/api-keys"]!["post"]!, "201");
        key["properties"]!["secret"]!["type"]!.GetValue<string>().ShouldBe("string");
        Resolve(key["properties"]!["class"]!)["enum"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["delivery", "preview", "management"]);
        Response(Paths[$"{Management}/api-keys/{{id}}"]!["get"]!, "200")["properties"]!.AsObject().ContainsKey("secret").ShouldBeFalse();
        Paths[$"{Management}/api-keys/{{id}}/expire"]!["post"]!["requestBody"]!["required"].ShouldBeNull();
        Paths[$"{Management}/api-keys/{{id}}/rotate"]!["post"]!["requestBody"]!["required"].ShouldBeNull();
        Response(Paths[$"{Management}/api-keys/{{id}}/rotate"]!["post"]!, "200")["properties"]!["secret"]!["type"]!.GetValue<string>().ShouldBe("string");
        Response(Paths[$"{Management}/api-keys/{{id}}"]!["get"]!, "200")["properties"]!.AsObject().ContainsKey("allowedAddresses").ShouldBeTrue();
        Response(Paths[$"{Management}/api-keys/{{id}}/allowed-addresses"]!["put"]!, "200")["properties"]!.AsObject().ContainsKey("previousKeyExpiresAt").ShouldBeTrue();

        Response(Paths[$"{Management}/jobs/{{id}}"]!["get"]!, "200")["properties"]!["history"]!["items"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/JobAttemptInfo");
        Response(Paths[$"{Management}/me"]!["get"]!, "200")["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["id", "kind", "name"]);
        Response(Paths[$"{Management}/activity"]!["get"]!, "200")["properties"]!["items"]!["items"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ActivityItem");

        var reset = Paths[$"{Management}/users/{{id}}/two-factor/reset"]!["post"]!;
        reset["requestBody"].ShouldBeNull();
        reset["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/UserInfo");
        reset["tags"]!.AsArray().Select(tag => tag!.GetValue<string>()).ShouldBe(["Users"]);

        foreach (var path in new[] { "users/{id}/logins", "me/logins" })
        {
            var login = Resolve(Response(Paths[$"{Management}/{path}"]!["get"]!, "200")["properties"]!["items"]!["items"]!);
            login["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldBe(["provider", "providerKey", "displayName"], path);
            Paths[$"{Management}/{path}/{{provider}}"]!["delete"]!["responses"]!["204"].ShouldNotBeNull(path);
        }
    }

    [Fact]
    public void NumbersAreNumbersAndNotNumericStrings()
    {
        var numbers = Nodes(Document).OfType<JsonObject>()
            .Where(node => node["type"] is JsonValue or JsonArray && node["type"]!.ToJsonString() is var type && (type.Contains("\"integer\"", StringComparison.Ordinal) || type.Contains("\"number\"", StringComparison.Ordinal)))
            .ToList();

        numbers.ShouldNotBeEmpty();
        numbers.ShouldAllBe(node => !node["type"]!.ToJsonString().Contains("\"string\"", StringComparison.Ordinal) && node["pattern"] == null);
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

    private JsonNode Resolve(JsonNode schema) =>
        schema["$ref"]?.GetValue<string>() is { } reference ? Schemas[reference["#/components/schemas/".Length..]]! : schema;

    private JsonNode Response(JsonNode operation, string status) => Resolve(operation["responses"]![status]!["content"]!["application/json"]!["schema"]!);

    private IEnumerable<JsonNode> QueryParameters(JsonNode operation) =>
        (operation["parameters"]?.AsArray() ?? [])
            .Select(parameter => parameter!["$ref"]?.GetValue<string>() is { } reference ? Parameters[reference["#/components/parameters/".Length..]]! : parameter!)
            .Where(parameter => parameter["in"]!.GetValue<string>() == "query");

    // The schema with its keys in order of name, since the writer's order is not part of the document.
    private static string Schema(JsonNode parameter) =>
        new JsonObject([.. parameter["schema"]!.AsObject().OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => KeyValuePair.Create(item.Key, item.Value?.DeepClone()))]).ToJsonString();

    private static IEnumerable<string> Values(JsonNode parameter) => parameter["schema"]!["enum"]!.AsArray().Select(item => item!.GetValue<string>());

    private static IEnumerable<JsonNode> Nodes(JsonNode? node) => node switch
    {
        JsonObject json => [json, .. json.SelectMany(item => Nodes(item.Value))],
        JsonArray array => [array, .. array.SelectMany(Nodes)],
        null => [],
        _ => [node],
    };

    private static IEnumerable<string> References(JsonNode? node) => node switch
    {
        JsonObject json => json.SelectMany(item => item.Key == "$ref" && item.Value is JsonValue value ? [value.GetValue<string>()] : References(item.Value)),
        JsonArray array => array.SelectMany(References),
        _ => [],
    };
}
