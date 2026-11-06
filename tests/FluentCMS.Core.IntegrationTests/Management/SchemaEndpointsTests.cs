using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema;
using FluentCMS.Samples.Blog;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// The Blog sample on SQLite, with a Super Admin signed in through the authorization server, who makes the roles the tests read the schema as.
public sealed class SchemaFixture : IAsyncLifetime
{
    public FluentCmsTestHost Host { get; } = new FluentCmsTestHost()
        .WithContentTypes([.. BlogSample.ContentTypes])
        .WithServices(services => services.Configure<RateLimitOptions>(limits =>
        {
            limits.Auth.PermitLimit = 10_000;
            limits.Management.PermitLimit = 10_000;
        }));

    public HttpClient Admin { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Admin = await ClientForAsync(BuiltInRoles.SuperAdminName);

    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        await Host.DisposeAsync();
    }

    public async Task<HttpClient> ClientForAsync(string role) => await Host.AsUserAsync(await Host.CreateUserAsync(role));

    // An instance-wide role with these grants, as the roles endpoint takes them, and a signed-in user who holds it.
    public async Task<HttpClient> ClientWithAsync(string grants)
    {
        var name = $"role-{Guid.NewGuid():N}";
        var created = await AdminFixture.SendAsync(Admin, HttpMethod.Post, "roles", $$"""{ "name": "{{name}}", "grants": {{grants}} }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        return await ClientForAsync(name);
    }

    public static Task<ApiResponse> GetAsync(string path, HttpClient client) => AdminFixture.SendAsync(client, HttpMethod.Get, path);
}

// The schema group of the Management API (#2785): permission schema.read, and the types and fields the caller may not read left out.
public sealed class SchemaEndpointsTests(SchemaFixture fixture) : IClassFixture<SchemaFixture>
{
    private const string UpdateVariable = "FLUENTCMS_UPDATE_SNAPSHOTS";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task TheBlogSampleMatchesTheStoredDocument()
    {
        var schema = await SchemaFixture.GetAsync("schema", fixture.Admin);
        schema.Status.ShouldBe(HttpStatusCode.OK, schema.Body?.ToJsonString());

        ShouldMatchStoredDocument(schema.Json, "blog-schema.json");

        var article = await SchemaFixture.GetAsync("schema/types/article", fixture.Admin);
        article.Status.ShouldBe(HttpStatusCode.OK, article.Body?.ToJsonString());
        JsonNode.DeepEquals(article.Json, schema.Json["types"]![0]).ShouldBeTrue();
    }

    [Fact]
    public async Task TheDocumentReadsBackIntoTheSdkRecords()
    {
        var schema = await SchemaFixture.GetAsync("schema", fixture.Admin);

        var described = schema.Json.Deserialize<SchemaDescription>(SdkJson)!;

        described.Types.Select(type => type.Name).ShouldBe(["article", "author", "tag"]);
        var videoUrl = described.Types[0].Fields.Single(field => field.Name == "videoUrl");
        videoUrl.VisibleWhen!.Operator.ShouldBe(ConditionOperator.Equal);
        videoUrl.VisibleWhen.Field.ShouldBe("category");
        described.Types[0].Fields.Single(field => field.Name == "sections").Settings[FieldSettings.Blocks].ToString().ShouldBe("""["hero","quote","gallery"]""");
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(described, SdkJson), schema.Json).ShouldBeTrue();
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("schema/types/article")]
    [InlineData("schema/types/unknown")]
    [InlineData("schema/snapshot")]
    public async Task ACallerWithoutSchemaReadIsRefused(string path)
    {
        using var reader = await fixture.ClientWithAsync("""[ { "permission": "content.*.read" } ]""");

        var refused = await SchemaFixture.GetAsync(path, reader);

        refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body?.ToJsonString());
        refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");

        using var anonymous = fixture.Host.CreateClient();
        (await SchemaFixture.GetAsync(path, anonymous)).Status.ShouldBe(HttpStatusCode.Unauthorized);

        var key = await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: TestContext.Current.CancellationToken);
        using var delivery = fixture.Host.AsKey(key);
        (await SchemaFixture.GetAsync(path, delivery)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ATypeTheCallerCannotReadIsAbsentAndAnswersAsAnUnknownOne()
    {
        using var reader = await fixture.ClientWithAsync("""[ { "permission": "schema.read" }, { "permission": "content.article.read" } ]""");

        var schema = (await SchemaFixture.GetAsync("schema", reader)).Json;

        Names(schema["types"]!).ShouldBe(["article"]);
        var fields = Names(schema["types"]![0]!["fields"]!);
        fields.ShouldNotContain("author");
        fields.ShouldNotContain("tags");
        fields.ShouldContain("seo");
        schema.ToJsonString().ShouldNotContain("\"author\"");
        schema.ToJsonString().ShouldNotContain("\"tag\"");
        Names(schema["components"]!).ShouldBe(["seo"]);
        Names(schema["blocks"]!).ShouldBe(["gallery", "hero", "quote"]);

        var unreadable = await SchemaFixture.GetAsync("schema/types/author", reader);
        var unknown = await SchemaFixture.GetAsync("schema/types/nothing", reader);
        unreadable.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        Problem(unreadable.Json, "author").ShouldBe(Problem(unknown.Json, "nothing"));

        var content = await AdminFixture.SendAsync(reader, HttpMethod.Get, "content/author/" + Guid.NewGuid().ToString("D"));
        Problem(content.Json, "author").ShouldBe(Problem(unreadable.Json, "author"));
    }

    [Fact]
    public async Task AFieldOutsideTheAllowListIsLeftOutNotBlanked()
    {
        using var reader = await fixture.ClientWithAsync("""
            [
              { "permission": "schema.read" },
              { "permission": "content.author.read" },
              { "permission": "content.article.read", "constraints": { "fields": ["title", "slug", "category", "videoUrl", "author"] } }
            ]
            """);

        var schema = (await SchemaFixture.GetAsync("schema", reader)).Json;

        var article = schema["types"]!.AsArray().Single(type => type!["name"]!.GetValue<string>() == "article")!;
        Names(article["fields"]!).ShouldBe(["title", "slug", "author", "category", "videoUrl"]);
        article["fields"]!.AsArray().ShouldAllBe(field => field is JsonObject);
        var author = schema["types"]!.AsArray().Single(type => type!["name"]!.GetValue<string>() == "author")!;
        author["reverseRelations"]!.AsArray().ShouldHaveSingleItem()!["sourceField"]!.GetValue<string>().ShouldBe("author");

        var described = await SchemaFixture.GetAsync("schema/types/article", reader);
        JsonNode.DeepEquals(described.Json, article).ShouldBeTrue();

        // Only the fields that went used the component. The blocks stay: the author's rich text bio may hold any of them inline.
        Names(schema["components"]!).ShouldBeEmpty();
        Names(schema["blocks"]!).ShouldBe(["gallery", "hero", "quote"]);
    }

    [Fact]
    public async Task AReverseSideGoesWithTheFieldItComesFrom()
    {
        using var reader = await fixture.ClientWithAsync("""
            [
              { "permission": "schema.read" },
              { "permission": "content.author.read" },
              { "permission": "content.article.read", "constraints": { "fields": ["title"] } }
            ]
            """);

        var author = await SchemaFixture.GetAsync("schema/types/author", reader);

        author.Status.ShouldBe(HttpStatusCode.OK, author.Body?.ToJsonString());
        author.Json["reverseRelations"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSnapshotIsTheInstanceSnapshotForAReaderOfEverything()
    {
        var snapshot = await SnapshotAsync(fixture.Admin);
        var instance = SchemaSnapshot.Of(fixture.Host.Services.GetRequiredService<SchemaModel>());

        snapshot.Text.ShouldBe(instance.Json);
        snapshot.ETag.ShouldBe($"\"{instance.Hash}\"");

        using var reader = await fixture.ClientWithAsync("""[ { "permission": "schema.read" }, { "permission": "content.tag.read" } ]""");
        var narrow = await SnapshotAsync(reader);
        narrow.ETag.ShouldBe($"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(narrow.Text)))}\"");
        narrow.Text.ShouldBe(SchemaJsonWriter.Write(new SchemaBuilder().AddSchemaJson(narrow.Text).Build()));
        var root = JsonNode.Parse(narrow.Text)!.AsObject();
        root.Select(pair => pair.Key).ShouldBe(["collections"]);
        Names(root["collections"]!).ShouldBe(["tag"]);
    }

    [Fact]
    public async Task AQueryParameterTheRoutesDoNotTakeIsRefused()
    {
        (await SchemaFixture.GetAsync("schema?depth=2", fixture.Admin)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await SchemaFixture.GetAsync("schema/types/article?populate=author", fixture.Admin)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await SchemaFixture.GetAsync("schema/snapshot?format=yaml", fixture.Admin)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static readonly JsonSerializerOptions SdkJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static List<string> Names(JsonNode array) => [.. array.AsArray().Select(item => item!["name"]!.GetValue<string>())];

    // The problem without what names the type or the request.
    private static string Problem(JsonNode problem, string type)
    {
        var copy = problem.DeepClone().AsObject();
        copy.Remove("traceId");
        copy.Remove("instance");
        return copy.ToJsonString().Replace(type, "{type}", StringComparison.Ordinal);
    }

    private static async Task<(string Text, string? ETag)> SnapshotAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri(AdminFixture.Root + "schema/snapshot", UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        return (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), response.Headers.ETag?.ToString());
    }

    private static void ShouldMatchStoredDocument(JsonNode actual, string name, [CallerFilePath] string callerPath = "")
    {
        var text = actual.ToJsonString(Indented);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(callerPath)!, "Snapshots", name), text + "\n", new UTF8Encoding(false));
            return;
        }

        var stored = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Management", "Snapshots", name));
        text.ShouldBe(stored.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'));
    }
}
