using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// The acceptance of #2784 over HTTP on SQLite, as an editor signed in with the management scope.
public sealed class ContentEndpointsTests(ManagementFixture fixture) : IClassFixture<ManagementFixture>
{
    [Fact]
    public async Task AnEditorCreatesPatchesPublishesAndUnpublishesAnEntry()
    {
        var created = await fixture.PostAsync("page", """{ "title": "Hello", "section": "news" }""");
        created.Status.ShouldBe(HttpStatusCode.Created);
        var id = created.Json["id"]!.GetValue<string>();
        created.Headers.Location!.ToString().ShouldBe($"{ManagementFixture.Root}page/{id}");
        created.ETag.ShouldBe("\"1\"");
        created.Json["status"]!.GetValue<string>().ShouldBe("draft");
        created.Json["locale"]!.GetValue<string>().ShouldBe("en");

        var patched = await fixture.PatchAsync($"page/{id}", """{ "title": "Hello world" }""", "\"1\"");
        patched.Status.ShouldBe(HttpStatusCode.OK);
        patched.ETag.ShouldBe("\"2\"");
        patched.Json["title"]!.GetValue<string>().ShouldBe("Hello world");
        patched.Json["section"]!.GetValue<string>().ShouldBe("news");

        var published = await fixture.PostAsync($"page/{id}/publish", ifMatch: "\"2\"");
        published.Status.ShouldBe(HttpStatusCode.OK);
        published.Json["entry"]!["status"]!.GetValue<string>().ShouldBe("published");
        published.Json["warnings"]!.AsArray().ShouldBeEmpty();

        var read = await fixture.GetAsync($"page/{id}?status=published");
        read.Json["title"]!.GetValue<string>().ShouldBe("Hello world");

        var unpublished = await fixture.PostAsync($"page/{id}/unpublish");
        unpublished.Status.ShouldBe(HttpStatusCode.OK);
        unpublished.Json["status"]!.GetValue<string>().ShouldBe("unpublished");
        (await fixture.GetAsync($"page/{id}?status=published")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AListAndAGetReadTheDraftsByDefault()
    {
        var id = await fixture.CreatePageAsync("Draft only", "drafts");

        var list = await fixture.GetAsync("page?filter[section][eq]=drafts");
        list.Status.ShouldBe(HttpStatusCode.OK);
        list.Json["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ShouldBe([id]);
        list.Json["total"]!.GetValue<long>().ShouldBe(1);
        (await fixture.GetAsync("page?filter[section][eq]=drafts&withTotal=false")).Json.AsObject().ContainsKey("total").ShouldBeFalse();

        var entry = await fixture.GetAsync($"page/{id}");
        entry.Json["status"]!.GetValue<string>().ShouldBe("draft");
        entry.ETag.ShouldBe("\"1\"");

        (await fixture.GetAsync("page?filter[section][eq]=drafts&status=published")).Json["items"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishWarnsAboutAnUnpublishedReference()
    {
        var person = await fixture.PostAsync("person", """{ "name": "Ada" }""");
        var personId = person.Json["id"]!.GetValue<string>();
        var id = await fixture.CreatePageAsync("Owned");
        await fixture.PatchAsync($"page/{id}", $$"""{ "owner": "{{personId}}" }""", "\"1\"");

        var published = await fixture.PostAsync($"page/{id}/publish");

        published.Status.ShouldBe(HttpStatusCode.OK);
        var warning = published.Json["warnings"]!.AsArray().ShouldHaveSingleItem()!;
        warning["path"]!.GetValue<string>().ShouldBe("owner");
        warning["code"]!.GetValue<string>().ShouldBe("FCMS2403");
        warning["targetId"]!.GetValue<string>().ShouldBe(personId);
    }

    [Fact]
    public async Task APatchMergesIntoAComponentAndANullRemovesAField()
    {
        var created = await fixture.PostAsync("page", """{ "title": "Meta", "section": "s", "meta": { "heading": "H", "description": "D" } }""");
        var id = created.Json["id"]!.GetValue<string>();
        created.Json["meta"]!["heading"]!.GetValue<string>().ShouldBe("H");

        var patched = await fixture.PatchAsync($"page/{id}", """{ "section": null, "meta": { "description": "New" } }""", "\"1\"");

        patched.Status.ShouldBe(HttpStatusCode.OK);
        patched.Json["section"].ShouldBeNull();
        patched.Json["meta"]!["heading"]!.GetValue<string>().ShouldBe("H");
        patched.Json["meta"]!["description"]!.GetValue<string>().ShouldBe("New");
    }

    [Fact]
    public async Task AReadThroughTheFallbackChainHasNoETag()
    {
        var id = await fixture.CreatePageAsync("English only");

        var french = await fixture.GetAsync($"page/{id}?locale=fr");

        french.Status.ShouldBe(HttpStatusCode.OK);
        french.Json["fallbacks"]!["title"]!.GetValue<string>().ShouldBe("en");
        french.ETag.ShouldBeNull();
        (await fixture.GetAsync($"page/{id}?locale=fr&fallback=none")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheLocationNamesTheEntryWhateverPathCreatedIt()
    {
        var created = await fixture.PostAsync("page/", """{ "title": "Slash" }""");

        created.Status.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location!.ToString().ShouldBe($"{ManagementFixture.Root}page/{created.Json["id"]!.GetValue<string>()}");
    }

    // Following the Location reads the site the entry was created in, not the default one.
    [Fact]
    public async Task TheLocationKeepsTheSiteTheRequestNamed()
    {
        var site = DefaultSite.Id.ToString("D");
        var created = await fixture.PostAsync($"page?site={site}", """{ "title": "Sited" }""");
        var unsited = await fixture.PostAsync("page", """{ "title": "Unsited" }""");

        var id = created.Json["id"]!.GetValue<string>();
        var location = created.Headers.Location!.ToString();
        location.ShouldBe($"{ManagementFixture.Root}page/{id}?site={site}");
        unsited.Headers.Location!.ToString().ShouldNotContain("?");
        var followed = await ManagementFixture.SendAsync(fixture.Editor, HttpMethod.Get, location[ManagementFixture.Root.Length..]);
        followed.Status.ShouldBe(HttpStatusCode.OK);
        followed.Json["id"]!.GetValue<string>().ShouldBe(id);
    }

    [Fact]
    public async Task ABodyOfAnotherMediaTypeIsUnsupported()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ManagementFixture.Root + "page", UriKind.Relative))
        {
            Content = new StringContent("""{ "title": "Plain" }""", System.Text.Encoding.UTF8, "text/plain"),
        };
        using var response = await fixture.Editor.SendAsync(request, TestContext.Current.CancellationToken);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        problem["code"]!.GetValue<string>().ShouldBe("FCMS3107");
        problem["title"]!.GetValue<string>().ShouldBe("Unsupported media type");
    }

    [Fact]
    public async Task AStaleIfMatchReturns412WithTheCurrentEntry()
    {
        var id = await fixture.CreatePageAsync("First");
        await fixture.PatchAsync($"page/{id}", """{ "title": "Second" }""", "\"1\"");

        var stale = await fixture.PatchAsync($"page/{id}", """{ "title": "Lost" }""", "\"1\"");

        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        stale.Json["code"]!.GetValue<string>().ShouldBe("FCMS3105");
        var current = stale.Json["current"]!;
        current["id"]!.GetValue<string>().ShouldBe(id);
        current["version"]!.GetValue<int>().ShouldBe(2);
        current["title"]!.GetValue<string>().ShouldBe("Second");
        current["status"]!.GetValue<string>().ShouldBe("draft");

        var publish = await fixture.PostAsync($"page/{id}/publish", ifMatch: "\"1\"");
        publish.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        publish.Json["current"]!["version"]!.GetValue<int>().ShouldBe(2);
        (await fixture.GetAsync($"page/{id}")).Json["title"]!.GetValue<string>().ShouldBe("Second");
    }

    [Theory]
    [InlineData(null, HttpStatusCode.PreconditionRequired, "FCMS3130")]
    [InlineData("*", HttpStatusCode.BadRequest, "FCMS3131")]
    [InlineData("W/*", HttpStatusCode.BadRequest, "FCMS3131")]
    [InlineData("\"1\", \"2\"", HttpStatusCode.BadRequest, "FCMS3131")]
    [InlineData("1", HttpStatusCode.BadRequest, "FCMS3131")]
    public async Task ASaveNeedsOneVersionInIfMatch(string? ifMatch, HttpStatusCode status, string code)
    {
        var id = await fixture.CreatePageAsync("Guarded");

        var refused = await fixture.PatchAsync($"page/{id}", """{ "title": "Unguarded" }""", ifMatch);

        refused.Status.ShouldBe(status);
        refused.Json["code"]!.GetValue<string>().ShouldBe(code);
        refused.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("If-Match");
    }

    // nginx with gzip on hands the client W/"1" for the ETag "1", which it then sends back.
    [Fact]
    public async Task ASaveBehindAProxyThatWeakensTheTagIsReadByItsVersion()
    {
        var id = await fixture.CreatePageAsync("Proxied");

        var saved = await fixture.PatchAsync($"page/{id}", """{ "title": "Through the proxy" }""", "W/\"1\"");
        var stale = await fixture.PatchAsync($"page/{id}", """{ "title": "Lost" }""", "W/\"1\"");

        saved.Status.ShouldBe(HttpStatusCode.OK);
        saved.ETag.ShouldBe("\"2\"");
        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        stale.Json["code"]!.GetValue<string>().ShouldBe("FCMS3105");
    }

    [Fact]
    public async Task APutReplacesTheDraft()
    {
        var id = await fixture.CreatePageAsync("Whole", "kept?");

        var replaced = await ManagementFixture.SendAsync(fixture.Editor, HttpMethod.Put, $"page/{id}", """{ "title": "Replaced" }""", "\"1\"");

        replaced.Status.ShouldBe(HttpStatusCode.OK);
        replaced.Json["title"]!.GetValue<string>().ShouldBe("Replaced");
        replaced.Json["section"].ShouldBeNull();
    }

    [Fact]
    public async Task VersionsListDiffAndRestorePerLocale()
    {
        var id = await fixture.CreatePageAsync("One");
        await fixture.PatchAsync($"page/{id}", """{ "title": "Two" }""", "\"1\"");
        var french = await fixture.PatchAsync($"page/{id}?locale=fr", """{ "title": "Un" }""", "\"0\"");
        french.Status.ShouldBe(HttpStatusCode.OK);
        french.Json["locale"]!.GetValue<string>().ShouldBe("fr");
        await fixture.PatchAsync($"page/{id}?locale=fr", """{ "title": "Deux" }""", "\"1\"");

        var english = await fixture.GetAsync($"page/{id}/versions");
        english.Status.ShouldBe(HttpStatusCode.OK);
        english.Json["items"]!.AsArray().Select(item => item!["number"]!.GetValue<int>()).ShouldBe([2, 1]);
        english.Json["items"]!.AsArray().ShouldAllBe(item => item!["locale"]!.GetValue<string>() == "en");
        english.Json["items"]![0]!["origin"]!.GetValue<string>().ShouldBe("api");

        var versions = await fixture.GetAsync($"page/{id}/versions?locale=fr");
        versions.Json["items"]!.AsArray().ShouldAllBe(item => item!["locale"]!.GetValue<string>() == "fr");

        var diff = await fixture.GetAsync($"page/{id}/versions/1/diff/2?locale=fr");
        diff.Status.ShouldBe(HttpStatusCode.OK);
        var change = diff.Json["changes"]!.AsArray().ShouldHaveSingleItem()!;
        change["path"]!.GetValue<string>().ShouldBe("title");
        change["kind"]!.GetValue<string>().ShouldBe("changed");
        change["before"]!.GetValue<string>().ShouldBe("Un");
        change["after"]!.GetValue<string>().ShouldBe("Deux");
        (await fixture.GetAsync($"page/{id}/versions/1/diff/2")).Json["changes"]![0]!["before"]!.GetValue<string>().ShouldBe("One");

        (await fixture.GetAsync($"page/{id}/versions/1?locale=fr")).Json["title"]!.GetValue<string>().ShouldBe("Un");

        var restored = await fixture.PostAsync($"page/{id}/versions/1/restore?locale=fr", ifMatch: "\"2\"");
        restored.Status.ShouldBe(HttpStatusCode.OK);
        restored.Json["title"]!.GetValue<string>().ShouldBe("Un");
        restored.Json["version"]!.GetValue<int>().ShouldBe(3);
        restored.ETag.ShouldBe("\"3\"");
        (await fixture.GetAsync($"page/{id}")).Json["title"]!.GetValue<string>().ShouldBe("Two");

        (await fixture.GetAsync($"page/{id}/versions/9")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.GetAsync($"page/{id}/versions?locale=all")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DiscardPutsThePublishedValuesBack()
    {
        var id = await fixture.CreatePageAsync("Kept");
        await fixture.PostAsync($"page/{id}/publish");
        await fixture.PatchAsync($"page/{id}", """{ "title": "Thrown away" }""", "\"1\"");

        var discarded = await fixture.PostAsync($"page/{id}/discard", ifMatch: "\"2\"");

        discarded.Status.ShouldBe(HttpStatusCode.OK);
        discarded.Json["title"]!.GetValue<string>().ShouldBe("Kept");
        discarded.Json["status"]!.GetValue<string>().ShouldBe("published");
    }

    [Fact]
    public async Task AScheduleAndItsCancelRunThroughTheJobRunner()
    {
        var host = fixture.Host;
        var scheduled = await fixture.CreatePageAsync("Later");
        var cancelled = await fixture.CreatePageAsync("Never");

        var at = host.Time.GetUtcNow().AddMinutes(10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var schedule = await fixture.PostAsync($"page/{scheduled}/schedule", $$"""{ "action": "publish", "at": "{{at}}" }""");
        schedule.Status.ShouldBe(HttpStatusCode.OK);
        schedule.Json["action"]!.GetValue<string>().ShouldBe("publish");
        schedule.Json["runAt"]!.GetValue<string>().ShouldBe(at);
        schedule.Json["locale"]!.GetValue<string>().ShouldBe("en");
        schedule.Json["version"]!.GetValue<int>().ShouldBe(1);

        (await fixture.PostAsync($"page/{cancelled}/schedule", $$"""{ "action": "publish", "at": "{{at}}" }""")).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.DeleteAsync($"page/{cancelled}/schedule/publish")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DeleteAsync($"page/{cancelled}/schedule/publish")).Status.ShouldBe(HttpStatusCode.Conflict);

        await fixture.RunDueJobsAsync();
        (await fixture.GetAsync($"page/{scheduled}")).Json["status"]!.GetValue<string>().ShouldBe("draft");

        host.Time.Advance(TimeSpan.FromMinutes(11));
        (await fixture.RunDueJobsAsync()).ShouldBeGreaterThanOrEqualTo(1);

        (await fixture.GetAsync($"page/{scheduled}")).Json["status"]!.GetValue<string>().ShouldBe("published");
        (await fixture.GetAsync($"page/{cancelled}")).Json["status"]!.GetValue<string>().ShouldBe("draft");
    }

    [Theory]
    [InlineData("""{ "action": "publish", "at": "2099-01-01T09:00:00" }""", "at")]
    [InlineData("""{ "action": "publish", "at": "2099-01-01T09:00:00Z", "timeZone": "Europe/Berlin" }""", "at")]
    [InlineData("""{ "action": "archive", "at": "2099-01-01T09:00:00Z" }""", "action")]
    [InlineData("""{ "action": "publish", "at": "2099-01-01T09:00:00Z", "when": "soon" }""", "when")]
    public async Task AScheduleBodyThatCannotBeReadIsRefused(string body, string path)
    {
        var id = await fixture.CreatePageAsync("Unscheduled");

        var refused = await fixture.PostAsync($"page/{id}/schedule", body);

        refused.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        refused.Json["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>()).ShouldBe([path]);
    }

    [Fact]
    public async Task AScheduleInATimeZoneTakesTheWallClockTime()
    {
        var id = await fixture.CreatePageAsync("Berlin");
        var year = fixture.Host.Time.GetUtcNow().Year + 1;

        var schedule = await fixture.PostAsync($"page/{id}/schedule", $$"""{ "action": "publish", "at": "{{year}}-07-01T09:00:00", "timeZone": "Europe/Berlin" }""");

        schedule.Status.ShouldBe(HttpStatusCode.OK);
        schedule.Json["runAt"]!.GetValue<string>().ShouldBe($"{year}-07-01T07:00:00Z");
        schedule.Json["timeZone"]!.GetValue<string>().ShouldBe("Europe/Berlin");
        (await fixture.DeleteAsync($"page/{id}/schedule/publish")).Status.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BulkByFilterRespectsTheCapAndReturnsAResultPerEntry()
    {
        var section = $"bulk-{Guid.NewGuid():N}"[..20];
        string[] ids = [await fixture.CreatePageAsync("A", section), await fixture.CreatePageAsync("B", section), await fixture.CreatePageAsync("C", section)];

        var over = await fixture.PostAsync($"page/bulk?filter[section][eq]={section}", """{ "operation": "publish", "cap": 2 }""");
        over.Status.ShouldBe(HttpStatusCode.BadRequest);
        over.Json["code"]!.GetValue<string>().ShouldBe("FCMS2207");
        over.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("cap");
        (await fixture.GetAsync($"page?status=published&filter[section][eq]={section}")).Json["items"]!.AsArray().ShouldBeEmpty();

        var bulk = await fixture.PostAsync($"page/bulk?filter[section][eq]={section}", """{ "operation": "publish", "cap": 3 }""");

        bulk.Status.ShouldBe(HttpStatusCode.OK);
        bulk.Json["cancelled"]!.GetValue<bool>().ShouldBeFalse();
        var items = bulk.Json["items"]!.AsArray();
        items.Select(item => item!["id"]!.GetValue<string>()).ShouldBe(ids, ignoreOrder: true);
        items.ShouldAllBe(item => item!["status"]!.GetValue<string>() == "succeeded" && item["version"]!.GetValue<int>() == 1);
        (await fixture.GetAsync($"page?status=published&filter[section][eq]={section}")).Json["items"]!.AsArray().Count.ShouldBe(3);
    }

    [Fact]
    public async Task BulkByIdReportsEachEntry()
    {
        var id = await fixture.CreatePageAsync("Bulk by id");
        var missing = Guid.CreateVersion7();

        var bulk = await fixture.PostAsync("page/bulk", $$"""{ "operation": "update", "items": [ { "id": "{{id}}", "patch": { "title": "Patched" }, "expectedVersion": 1 }, { "id": "{{id}}", "patch": { "title": "Again" }, "expectedVersion": 1 }, { "id": "{{missing}}", "patch": { "title": "Nobody" }, "expectedVersion": 1 } ] }""");

        bulk.Status.ShouldBe(HttpStatusCode.OK);
        var items = bulk.Json["items"]!.AsArray();
        items.Select(item => item!["status"]!.GetValue<string>()).ShouldBe(["succeeded", "duplicate", "failed"]);
        items[0]!["version"]!.GetValue<int>().ShouldBe(2);
        items[2]!["problem"]!["status"]!.GetValue<int>().ShouldBe(404);
        items[2]!["problem"]!["code"]!.GetValue<string>().ShouldBe("FCMS3104");
        items[2]!["problem"]!["type"]!.GetValue<string>().ShouldBe("urn:fluentcms:error:FCMS3104");
        items[2]!["problem"]!.AsObject().ContainsKey("traceId").ShouldBeFalse();
        (await fixture.GetAsync($"page/{id}")).Json["title"]!.GetValue<string>().ShouldBe("Patched");
    }

    [Fact]
    public async Task ABulkRequestOutsideItsGrammarIsRefused()
    {
        var sorted = await fixture.PostAsync("page/bulk?filter[section][eq]=x&sort=title", """{ "operation": "delete", "cap": 1 }""");
        sorted.Status.ShouldBe(HttpStatusCode.BadRequest);
        sorted.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("sort");

        var field = await fixture.PostAsync("page/bulk?filter[nope][eq]=x", """{ "operation": "delete", "cap": 1 }""");
        field.Status.ShouldBe(HttpStatusCode.BadRequest);
        field.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("filter[nope][eq]");

        var unknown = await fixture.PostAsync("page/bulk", """{ "operation": "delete", "items": [], "everything": true }""");
        unknown.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("everything");
    }

    [Fact]
    public async Task TrashNeedsRestoreOrPurgeOnTheType()
    {
        using var deleter = await fixture.ClientWithAsync("content.page.read", "content.page.create", "content.page.delete");
        using var purger = await fixture.ClientWithAsync("content.page.read", "content.page.purge");
        var id = await fixture.CreatePageAsync("Binned");
        (await fixture.DeleteAsync($"page/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.GetAsync($"page/{id}")).Status.ShouldBe(HttpStatusCode.NotFound);

        (await ManagementFixture.SendAsync(deleter, HttpMethod.Get, "page/trash")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await ManagementFixture.SendAsync(deleter, HttpMethod.Post, $"page/trash/{id}/restore")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await ManagementFixture.SendAsync(deleter, HttpMethod.Delete, $"page/trash/{id}")).Status.ShouldBe(HttpStatusCode.Forbidden);

        var trash = await fixture.GetAsync("page/trash");
        trash.Status.ShouldBe(HttpStatusCode.OK);
        var binned = trash.Json["items"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
        binned["deletedAt"].ShouldNotBeNull();

        var restored = await fixture.PostAsync($"page/trash/{id}/restore?locale=all");
        restored.Status.ShouldBe(HttpStatusCode.OK);
        restored.Json["title"]!.GetValue<string>().ShouldBe("Binned");
        (await fixture.GetAsync($"page/{id}")).Status.ShouldBe(HttpStatusCode.OK);

        (await fixture.DeleteAsync($"page/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.DeleteAsync($"page/trash/{id}")).Status.ShouldBe(HttpStatusCode.Forbidden);

        (await ManagementFixture.SendAsync(purger, HttpMethod.Get, "page/trash")).Status.ShouldBe(HttpStatusCode.OK);
        (await ManagementFixture.SendAsync(purger, HttpMethod.Delete, $"page/trash/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.GetAsync("page/trash")).Json["items"]!.AsArray().ShouldNotContain(item => item!["id"]!.GetValue<string>() == id);
        (await ManagementFixture.SendAsync(purger, HttpMethod.Delete, $"page/trash/{id}")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheGroupNeedsTheManagementScope()
    {
        using var anonymous = fixture.Host.CreateClient();
        var unauthenticated = await ManagementFixture.SendAsync(anonymous, HttpMethod.Get, "page");
        unauthenticated.Status.ShouldBe(HttpStatusCode.Unauthorized);
        unauthenticated.Json["code"]!.GetValue<string>().ShouldBe("FCMS3102");

        using var delivery = fixture.Host.AsKey(await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Delivery, "Editor", cancellationToken: TestContext.Current.CancellationToken));
        var forbidden = await ManagementFixture.SendAsync(delivery, HttpMethod.Get, "page");
        forbidden.Status.ShouldBe(HttpStatusCode.Forbidden);
        forbidden.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");

        using var management = fixture.Host.AsKey(await fixture.Host.CreateApiKeyAsync(ApiKeyClass.Management, "Editor", cancellationToken: TestContext.Current.CancellationToken));
        (await ManagementFixture.SendAsync(management, HttpMethod.Post, "page", """{ "title": "By key" }""")).Status.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RequestsTheRoutesCannotPlaceAreRefused()
    {
        var unknown = await fixture.PostAsync("nothing", """{ "title": "x" }""");
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("type");
        (await fixture.GetAsync("nothing")).Status.ShouldBe(HttpStatusCode.NotFound);

        (await fixture.GetAsync("page/not-an-id")).Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("id");
        (await fixture.PostAsync("page?locale=all", """{ "title": "Every locale" }""")).Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("locale");
        (await fixture.PostAsync("page?status=draft", """{ "title": "Draft" }""")).Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("status");
        (await fixture.PostAsync("page", """{ "title": "One", "title": "Two" }""")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.PostAsync("page", "[]")).Status.ShouldBe(HttpStatusCode.BadRequest);

        var invalid = await fixture.PostAsync("page", """{ "title": "Fine", "nope": 1 }""");
        invalid.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        invalid.Json["errors"]![0]!["path"]!.GetValue<string>().ShouldBe("nope");
    }
}
