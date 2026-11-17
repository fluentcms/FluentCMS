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
    private const string NotFound = """{"status":404,"code":"FCMS3104","title":"Not found"}""";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryCallCarriesTheTokenTheSiteAndTheLocaleUnderTheManagementPath()
    {
        using var harness = new ApiHarness("https://example.com/cms");
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Server.Answer(HttpStatusCode.OK, """{"items":[],"pageSize":20}""");

        await harness.Client.ListEntriesAsync("article", null, Token);

        var sent = harness.Server.Requests.Single();
        sent.Uri.ShouldBe(new Uri("https://example.com/cms/api/management/v1/content/article"));
        sent.Header("Authorization").ShouldBe("Bearer token-1");
        sent.Header("X-Cms-Site").ShouldBe("blog");
        sent.Header("X-Cms-Locale").ShouldBe("fr");
    }

    [Fact]
    public async Task TheCallersOwnReadsSendNeitherASiteNorALocale()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Server.Answer(HttpStatusCode.OK, Me).Answer(HttpStatusCode.OK, """{"items":[]}""");

        var me = await harness.Client.GetMeAsync(Token);
        await harness.Client.GetMySitesAsync(Token);

        me.Roles.Single().Name.ShouldBe("Super Admin");
        harness.Server.Requests.ShouldAllBe(sent => sent.Header("X-Cms-Locale") == null && sent.Header("X-Cms-Site") == null);
    }

    [Fact]
    public async Task AReadOfTheSchemaForTheDefaultSiteSendsNeitherTheSessionsSiteNorItsLocale()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Server.Answer(HttpStatusCode.OK, """{"types":[],"components":[],"blocks":[]}""");

        await harness.Client.GetSchemaAsync(null, null, Token);

        var sent = harness.Server.Requests.Single();
        (sent.Header("X-Cms-Site"), sent.Header("X-Cms-Locale")).ShouldBe((null, null));
    }

    [Fact]
    public async Task ASaveIsSentInTheSiteAndLocaleOfItsReadAfterTheViewSwitched()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Session.Locale = "en";
        harness.Server.Answer(HttpStatusCode.OK, Entry, configure: response => response.Headers.TryAddWithoutValidation("ETag", "\"3\""))
            .Answer(HttpStatusCode.OK, Entry, configure: response => response.Headers.TryAddWithoutValidation("ETag", "\"4\""));

        var read = await harness.Client.GetEntryAsync("article", Guid.NewGuid(), null, Token);
        harness.Session.Site = "shop";
        harness.Session.Locale = "fr";
        var saved = await harness.Client.SaveEntryAsync("article", read.Entry!.Id, read.Entry.Fields, read, Token);

        harness.Server.Requests.ShouldAllBe(sent => sent.Header("X-Cms-Site") == "blog" && sent.Header("X-Cms-Locale") == "en");
        (saved.Site, saved.Locale, saved.EntityTag).ShouldBe(("blog", "en", "\"4\""));
    }

    [Fact]
    public async Task ARequestThatNamesItsSiteSendsOnlyTheLocaleItNames()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Session.Token = null;
        harness.Server.Answer(HttpStatusCode.NoContent).Answer(HttpStatusCode.NoContent).Answer(HttpStatusCode.NoContent);

        await harness.Api.SendAsync(new ApiRequest(HttpMethod.Delete, "content/tag/1") { Site = "shop", Locale = "de" }, Token);
        await harness.Api.SendAsync(new ApiRequest(HttpMethod.Delete, "content/tag/2") { Site = "shop" }, Token);
        await harness.Api.SendAsync(new ApiRequest(HttpMethod.Delete, "content/tag/3") { NoSite = true, NoLocale = true }, Token);

        var sent = harness.Server.Requests;
        (sent[0].Header("X-Cms-Site"), sent[0].Header("X-Cms-Locale")).ShouldBe(("shop", "de"));
        (sent[1].Header("X-Cms-Site"), sent[1].Header("X-Cms-Locale")).ShouldBe(("shop", null));
        (sent[2].Header("X-Cms-Site"), sent[2].Header("X-Cms-Locale")).ShouldBe((null, null));
        sent[0].Header("Authorization").ShouldBeNull();
    }

    [Fact]
    public async Task ARequestCannotNameASiteOrLocaleAndSendNone()
    {
        using var harness = new ApiHarness();

        await Should.ThrowAsync<ArgumentException>(() => harness.Api.SendAsync(new ApiRequest(HttpMethod.Get, "me") { Site = "shop", NoSite = true }, Token));
        await Should.ThrowAsync<ArgumentException>(() => harness.Api.SendAsync(new ApiRequest(HttpMethod.Get, "me") { Locale = "fr", NoLocale = true }, Token));
        harness.Server.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/api/management/v1/me")]
    [InlineData("https://elsewhere.example/me")]
    [InlineData("\\/evil.example/x")]
    [InlineData(" //evil.example/x")]
    [InlineData("\t//evil.example/x")]
    [InlineData("content\\..\\..\\x")]
    [InlineData("me\r\nX-Injected: 1")]
    [InlineData("..%2f..%2f..%2fconnect/token")]
    [InlineData("..%2F..%2F..%2Fconnect/token")]
    [InlineData("..%5c..%5c..%5cconnect/token")]
    [InlineData("..%5C..%5C..%5Cconnect/token")]
    [InlineData("content/%2e%2e%2f%2e%2e/x")]
    public void APathIsRelativeToTheManagementApi(string path) =>
        Should.Throw<ArgumentException>(() => new ApiRequest(HttpMethod.Get, path));

    [Fact]
    public void AnEncodedSlashInTheQueryIsAValue() =>
        new ApiRequest(HttpMethod.Get, "content/article?filter[slug][eq]=a%2Fb").Path.ShouldEndWith("a%2Fb");

    [Theory]
    [InlineData("../../../other/x")]
    [InlineData("content/../../../../connect/token")]
    [InlineData("content/%2e%2e/%2e%2e/%2e%2e/%2e%2e/connect/token")]
    [InlineData("//evil.example/x")]
    [InlineData("x:y")]
    [InlineData("http:evil")]
    public async Task APathThatLeadsOutOfTheManagementApiIsNotSent(string path)
    {
        using var harness = new ApiHarness("https://example.com/cms");

        await Should.ThrowAsync<ArgumentException>(() => harness.Api.SendAsync(new ApiRequest(HttpMethod.Get, path), Token));

        harness.Server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A422BecomesAProblemWithThePathOfEachError()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.UnprocessableEntity, """
            {"type":"urn:fluentcms:error:FCMS2103","title":"Too long","status":422,"detail":"Must have at most 50 characters, has 60.","code":"FCMS2103",
             "errors":[{"path":"name","code":"FCMS2103","message":"Must have at most 50 characters, has 60."}],"traceId":"0HNQ"}
            """);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.CreateEntryAsync("tag", new JsonObject { ["name"] = "x" }, harness.Client.StartAction(), Token));

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

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.SaveEntryAsync("article", Guid.NewGuid(), [], Based("\"2\""), Token));

        thrown.Problem.Status.ShouldBe(412);
        thrown.Problem.Current!["version"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public async Task TheProblemMediaTypeIsReadInAnyCase()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.NotFound, NotFound, "Application/Problem+JSON");

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetMeAsync(Token));

        thrown.Problem.Code.ShouldBe("FCMS3104");
    }

    [Theory]
    [InlineData("""{"status":412,"code":"FCMS3105","title":"Version conflict","current":"not an entry","traceId":"0HNQ"}""")]
    [InlineData("""{"status":412,"code":"FCMS3105","title":"Version conflict","errors":"not a list","traceId":"0HNQ"}""")]
    public async Task AProblemWithAMemberOfTheWrongTypeKeepsTheOthers(string body)
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.PreconditionFailed, body);

        var thrown = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetMeAsync(Token));

        thrown.Problem.Code.ShouldBe("FCMS3105");
        thrown.Problem.Title.ShouldBe("Version conflict");
        thrown.Problem.TraceId.ShouldBe("0HNQ");
        thrown.Problem.Current.ShouldBeNull();
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
    public async Task ASuccessThatIsNotJsonThrowsAJsonException()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, "<!doctype html><title>Admin</title>", "text/html");

        await Should.ThrowAsync<JsonException>(() => harness.Client.GetMeAsync(Token));
    }

    [Fact]
    public async Task AReadKeepsTheEntityTagByItsValueWithoutFallbacksAndASaveSendsItAsIfMatch()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Entry, configure: response => response.Headers.TryAddWithoutValidation("ETag", "W/\"3\""));
        harness.Server.Answer(HttpStatusCode.OK, Entry);

        var read = await harness.Client.GetEntryAsync("article", Guid.NewGuid(), "fr", Token);
        await harness.Client.SaveEntryAsync("article", read.Entry!.Id, read.Entry.Fields, read, Token);

        read.IsNewLocale.ShouldBeFalse();
        read.EntityTag.ShouldBe("\"3\"");
        read.Entry.Version.ShouldBe(3);
        read.Entry.Fields["title"]!.GetValue<string>().ShouldBe("Bonjour");
        harness.Server.Requests[0].Uri.Query.ShouldBe("?fallback=none");
        var save = harness.Server.Requests[1];
        save.Method.ShouldBe(HttpMethod.Put);
        save.Header("If-Match").ShouldBe("\"3\"");
        save.Body.ShouldBe("""{"title":"Bonjour"}""");
    }

    [Fact]
    public async Task ALocaleTheEntryLacksIsReadAsANewLocaleAndAMissingEntryIsAProblem()
    {
        using var harness = new ApiHarness();
        harness.Server.Problem(HttpStatusCode.NotFound, NotFound).Answer(HttpStatusCode.OK, """{"id":"01a1e1fa-4db2-7614-9c93-b8a7f6254fa5"}""");
        harness.Server.Problem(HttpStatusCode.NotFound, NotFound).Problem(HttpStatusCode.NotFound, NotFound);

        var added = await harness.Client.GetEntryAsync("article", Guid.NewGuid(), "fr", Token);
        var missing = await Should.ThrowAsync<ApiProblemException>(() => harness.Client.GetEntryAsync("article", Guid.NewGuid(), "fr", Token));

        added.IsNewLocale.ShouldBeTrue();
        added.EntityTag.ShouldBe(ApiRequest.NewLocale);
        added.Locale.ShouldBe("fr");
        missing.Problem.Status.ShouldBe(404);
        var check = harness.Server.Requests[1];
        (check.Uri.Query, check.Header("X-Cms-Locale")).ShouldBe(("?fields=id", "all"));
    }

    // A read made by a test, in site blog and locale fr, with the tag it gives.
    private static EntryRead Based(string entityTag) => new(null, entityTag, "blog", "fr");

    [Theory]
    [InlineData(ApiRequest.NewLocale, "\"0\"")]
    [InlineData("7", "\"7\"")]
    [InlineData("W/\"7\"", "\"7\"")]
    public async Task IfMatchIsSentAsAStrongTag(string given, string sent)
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Entry);

        await harness.Client.SaveEntryAsync("article", Guid.NewGuid(), [], Based(given), Token);

        harness.Server.Requests.Single().Header("If-Match").ShouldBe(sent);
    }

    [Fact]
    public async Task ARetryOfACreateSendsTheKeySiteAndLocaleOfItsUserAction()
    {
        using var harness = new ApiHarness();
        harness.Session.Site = "blog";
        harness.Session.Locale = "fr";
        harness.Server.Answer(HttpStatusCode.Created, Entry).Answer(HttpStatusCode.Created, Entry);
        var action = harness.Client.StartAction();

        await harness.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Bonjour" }, action, Token);
        harness.Session.Site = "shop";
        harness.Session.Locale = "de";
        await harness.Client.CreateEntryAsync("article", new JsonObject { ["title"] = "Bonjour" }, action, Token);

        harness.Server.Requests.Select(request => request.Header("Idempotency-Key")).ShouldBe([action.IdempotencyKey, action.IdempotencyKey]);
        harness.Server.Requests.ShouldAllBe(request => request.Header("X-Cms-Site") == "blog" && request.Header("X-Cms-Locale") == "fr");
        harness.Server.Requests.Select(request => request.Body).Distinct().ShouldHaveSingleItem();
        harness.Client.StartAction().IdempotencyKey.ShouldNotBe(action.IdempotencyKey);
    }

    [Fact]
    public async Task AnActionStartedOnTheDefaultSiteAndLocaleStaysThere()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.Created, Entry);
        var action = harness.Client.StartAction();
        harness.Session.Site = "shop";
        harness.Session.Locale = "de";

        await harness.Client.CreateEntryAsync("article", [], action, Token);

        var sent = harness.Server.Requests.Single();
        (sent.Header("X-Cms-Site"), sent.Header("X-Cms-Locale")).ShouldBe((null, null));
    }

    [Fact]
    public async Task AWriteLeavesOutTheSystemMembersOfTheFormsDocument()
    {
        using var harness = new ApiHarness();
        harness.Server.Answer(HttpStatusCode.OK, Entry);

        await harness.Client.SaveEntryAsync("article", Guid.NewGuid(), new JsonObject { ["id"] = "x", ["deletedAt"] = "2026-01-01T00:00:00Z", ["title"] = "Bonjour" }, Based("\"3\""), Token);

        harness.Server.Requests.Single().Body.ShouldBe("""{"title":"Bonjour"}""");
    }

    [Fact]
    public void AnEntryKeepsItsSystemMembersApartFromItsFieldsAndWritesBackAsOneObject()
    {
        const string read = """
            {"id":"01a1e1fa-4db2-7614-9c93-b8a7f6254fa5","status":"draft","locale":"fr","version":3,"createdBy":"01a1e231-1c00-7790-ba13-eac6571f2a3b",
             "updatedBy":"01a1e231-1c00-7790-ba13-eac6571f2a3b","deletedAt":"2026-11-16T02:30:20Z","deletedBy":"01a1e231-1c00-7790-ba13-eac6571f2a3b",
             "fallbacks":{"body":"en"},"title":"Bonjour","body":null}
            """;
        var entry = JsonSerializer.Deserialize(read, ManagementJsonContext.Default.ContentEntry)!;

        entry.Id.ShouldBe(Guid.Parse("01a1e1fa-4db2-7614-9c93-b8a7f6254fa5"));
        (entry.Status, entry.Locale, entry.Version).ShouldBe(("draft", "fr", 3));
        entry.CreatedBy.ShouldBe(Guid.Parse("01a1e231-1c00-7790-ba13-eac6571f2a3b"));
        entry.UpdatedBy.ShouldBe(entry.CreatedBy);
        entry.DeletedBy.ShouldBe(entry.CreatedBy);
        entry.DeletedAt.ShouldBe(new DateTimeOffset(2026, 11, 16, 2, 30, 20, TimeSpan.Zero));
        entry.Fallbacks.ShouldBe(new Dictionary<string, string> { ["body"] = "en" });
        entry.Fields.Select(field => field.Key).ShouldBe(["title", "body"]);

        var written = JsonNode.Parse(JsonSerializer.Serialize(entry with { Fields = new JsonObject { ["id"] = "x", ["title"] = "Bonjour" } }, ManagementJsonContext.Default.ContentEntry))!.AsObject();
        written["id"]!.GetValue<string>().ShouldBe("01a1e1fa-4db2-7614-9c93-b8a7f6254fa5");
        written["version"]!.GetValue<int>().ShouldBe(3);
        written["fallbacks"]!["body"]!.GetValue<string>().ShouldBe("en");
        written["title"]!.GetValue<string>().ShouldBe("Bonjour");
        JsonSerializer.Deserialize(written.ToJsonString(), ManagementJsonContext.Default.ContentEntry)!.Fields.Select(field => field.Key).ShouldBe(["title"]);
    }

    [Theory]
    [InlineData("""{"id":42}""")]
    [InlineData("""{"version":"three"}""")]
    [InlineData("""{"createdAt":true}""")]
    [InlineData("""{"fallbacks":["en"]}""")]
    [InlineData("""[1]""")]
    public void AnEntryWithASystemMemberOfTheWrongTypeIsAJsonException(string read) =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize(read, ManagementJsonContext.Default.ContentEntry));

    [Theory]
    [InlineData(null)]
    [InlineData("file:///C:/cms")]
    [InlineData("ftp://cms.example.com")]
    [InlineData("https://cms.example.com/cms?tenant=1")]
    [InlineData("https://cms.example.com/cms#admin")]
    public void AnAddressThatIsNotHttpWithoutQueryOrFragmentIsRefused(string? address) =>
        Should.Throw<InvalidOperationException>(() => new ManagementApiOptions { ApiBaseUrl = address is null ? null : new Uri(address) }.ManagementAddress())
            .Message.ShouldContain("ApiBaseUrl");

    [Theory]
    [InlineData("https://cms.example.com", "https://cms.example.com/api/management/v1/")]
    [InlineData("http://localhost:8080/", "http://localhost:8080/api/management/v1/")]
    [InlineData("https://example.com/cms", "https://example.com/cms/api/management/v1/")]
    public void TheManagementAddressKeepsThePathBase(string address, string management) =>
        new ManagementApiOptions { ApiBaseUrl = new Uri(address) }.ManagementAddress().ShouldBe(new Uri(management));
}
