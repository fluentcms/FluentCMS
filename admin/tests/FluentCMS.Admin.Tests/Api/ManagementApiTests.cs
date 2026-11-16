using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Api;

public sealed class ManagementApiTests
{
    private const string Me = """{"id":"01a1e1f8-ff43-7810-88a0-6e0124e0827a","kind":"adminUser","name":"Ada","roles":[{"id":"00000000-0000-7000-8000-000000000001","name":"Super Admin","siteId":null}]}""";
    private const string Entry = """{"id":"01a1e1fa-4db2-7614-9c93-b8a7f6254fa5","status":"draft","locale":"fr","version":3,"title":"Bonjour"}""";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryCallCarriesTheTokenTheSiteAndTheLocaleUnderTheManagementPath()
    {
        using var harness = new ApiHarness("https://example.com/cms");
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Server.Answer(HttpStatusCode.OK, Me);

        var me = await harness.Client.GetMeAsync(Token);

        me.Name.ShouldBe("Ada");
        me.Roles.Single().Name.ShouldBe("Super Admin");
        var sent = harness.Server.Requests.Single();
        sent.Uri.ShouldBe(new Uri("https://example.com/cms/api/management/v1/me"));
        sent.Header("Authorization").ShouldBe("Bearer token-1");
        sent.Header("X-Cms-Site").ShouldBe("blog");
        sent.Header("X-Cms-Locale").ShouldBe("fr");
    }

    [Fact]
    public async Task ARequestThatNamesItsSiteAndLocaleKeepsThem()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Session.Token = null;
        harness.Server.Answer(HttpStatusCode.NoContent);

        await harness.Api.SendAsync(new ApiRequest(HttpMethod.Delete, "content/tag/1") { Site = "shop", Locale = "de" }, Token);

        var sent = harness.Server.Requests.Single();
        sent.Header("X-Cms-Site").ShouldBe("shop");
        sent.Header("X-Cms-Locale").ShouldBe("de");
        sent.Header("Authorization").ShouldBeNull();
    }

    [Fact]
    public void APathIsRelativeToTheManagementApi()
    {
        Should.Throw<ArgumentException>(() => new ApiRequest(HttpMethod.Get, "/api/management/v1/me"));
        Should.Throw<ArgumentException>(() => new ApiRequest(HttpMethod.Get, "https://elsewhere.example/me"));
    }

    [Fact]
    public async Task A422BecomesAProblemWithThePathOfEachError()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.UnprocessableEntity, """
            {"type":"urn:fluentcms:error:FCMS2103","title":"Too long","status":422,"detail":"Must have at most 50 characters, has 60.","code":"FCMS2103",
             "errors":[{"path":"name","code":"FCMS2103","message":"Must have at most 50 characters, has 60."}],"traceId":"0HNQ"}
            """);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.CreateEntryAsync("tag", new JsonObject { ["name"] = "x" }, "key-1", null, Token));

        thrown.Problem.Status.ShouldBe(422);
        thrown.Problem.Code.ShouldBe("FCMS2103");
        thrown.Problem.Title.ShouldBe("Too long");
        thrown.Problem.TraceId.ShouldBe("0HNQ");
        thrown.Problem.Errors.ShouldHaveSingleItem().Path.ShouldBe("name");
    }

    [Fact]
    public async Task A412CarriesTheEntryAsItIsNow()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.PreconditionFailed, $$"""{"status":412,"code":"FCMS3105","title":"Version conflict","current":{{Entry}}}""");

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.SaveEntryAsync("article", Guid.NewGuid(), [], "\"2\"", "fr", Token));

        thrown.Problem.Status.ShouldBe(412);
        thrown.Problem.Current!["version"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public async Task AnAnswerThatIsNotAProblemKeepsItsStatus()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.BadGateway, "<html>proxy</html>", "text/html");

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetMeAsync(Token));

        thrown.Problem.Status.ShouldBe(502);
        thrown.Problem.Code.ShouldBeNull();
    }

    [Fact]
    public async Task AReadKeepsTheEntityTagByItsValueAndASaveSendsItAsIfMatch()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Entry, configure: response => response.Headers.TryAddWithoutValidation("ETag", "W/\"3\""));
        harness.Server.Answer(HttpStatusCode.OK, Entry);

        var read = await harness.Client.GetEntryAsync("article", Guid.NewGuid(), "fr", Token);
        await harness.Client.SaveEntryAsync("article", read.Value.Id, read.Value.Fields, read.EntityTag!, "fr", Token);

        read.EntityTag.ShouldBe("\"3\"");
        read.Value.Version.ShouldBe(3);
        read.Value.Fields["title"]!.GetValue<string>().ShouldBe("Bonjour");
        var save = harness.Server.Requests[1];
        save.Method.ShouldBe(HttpMethod.Put);
        save.Header("If-Match").ShouldBe("\"3\"");
        save.Body.ShouldBe("""{"title":"Bonjour"}""");
    }

    [Theory]
    [InlineData(ApiRequest.NewLocale, "\"0\"")]
    [InlineData("7", "\"7\"")]
    [InlineData("W/\"7\"", "\"7\"")]
    public async Task IfMatchIsSentAsAStrongTag(string given, string sent)
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Entry);

        await harness.Client.SaveEntryAsync("article", Guid.NewGuid(), [], given, "fr", Token);

        harness.Server.Requests.Single().Header("If-Match").ShouldBe(sent);
    }

    [Fact]
    public async Task ARetryOfACreateSendsTheKeyOfItsUserAction()
    {
        using var harness = new ApiHarness();
        var key = ApiRequest.NewIdempotencyKey();
        harness.Server.Answer(HttpStatusCode.Created, Entry).Answer(HttpStatusCode.Created, Entry);

        await harness.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Bonjour" }, key, null, Token);
        await harness.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Bonjour" }, key, null, Token);

        harness.Server.Requests.Select(request => request.Header("Idempotency-Key")).ShouldBe([key, key]);
        harness.Server.Requests.Select(request => request.Body).Distinct().ShouldHaveSingleItem();
        ApiRequest.NewIdempotencyKey().ShouldNotBe(key);
    }

    [Fact]
    public void AnEntryKeepsItsSystemMembersApartFromItsFieldsAndWritesBackAsOneObject()
    {
        var entry = JsonSerializer.Deserialize(Entry, ManagementJsonContext.Default.ContentEntry)!;

        entry.Id.ShouldBe(Guid.Parse("01a1e1fa-4db2-7614-9c93-b8a7f6254fa5"));
        entry.Status.ShouldBe("draft");
        entry.Locale.ShouldBe("fr");
        entry.Version.ShouldBe(3);
        entry.Fields.Select(field => field.Key).ShouldBe(["title"]);
        var written = JsonNode.Parse(JsonSerializer.Serialize(entry, ManagementJsonContext.Default.ContentEntry))!.AsObject();
        written["title"]!.GetValue<string>().ShouldBe("Bonjour");
        written["version"]!.GetValue<int>().ShouldBe(3);
        written["publishedAt"].ShouldBeNull();
    }

    [Fact]
    public void WithoutABaseAddressTheClientSaysWhatIsMissing() =>
        Should.Throw<InvalidOperationException>(() => new ManagementApiOptions().ManagementAddress()).Message.ShouldContain("ApiBaseUrl");
}
