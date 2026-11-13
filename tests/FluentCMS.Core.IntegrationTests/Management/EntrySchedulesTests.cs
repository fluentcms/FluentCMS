using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Management;

// The acceptance of #3094 over HTTP on SQLite: every entry the Management API answers carries the pending scheduled
// publish and unpublish of its locale, and Delivery answers neither.
public sealed class EntrySchedulesTests(ManagementFixture fixture) : IClassFixture<ManagementFixture>
{
    [Fact]
    public async Task SchedulingReschedulingAndCancellingShowInTheNextRead()
    {
        var id = await fixture.CreatePageAsync("Planned", "planned");
        var (soon, later, withdrawn) = (At(10), At(20), At(30));

        var unscheduled = await fixture.GetAsync($"page/{id}");
        unscheduled.Json.AsObject().ContainsKey("scheduledPublishAt").ShouldBeTrue();
        unscheduled.Json["scheduledPublishAt"].ShouldBeNull();
        unscheduled.Json["scheduledUnpublishAt"].ShouldBeNull();

        await ScheduleAsync(id, "publish", soon);
        (await fixture.GetAsync($"page/{id}")).Json["scheduledPublishAt"]!.GetValue<string>().ShouldBe(soon);
        var listed = Item(await fixture.GetAsync("page?filter[section][eq]=planned"), id);
        listed["scheduledPublishAt"]!.GetValue<string>().ShouldBe(soon);
        listed["scheduledUnpublishAt"].ShouldBeNull();

        await ScheduleAsync(id, "publish", later);
        await ScheduleAsync(id, "unpublish", withdrawn);
        var moved = await fixture.GetAsync($"page/{id}");
        moved.Json["scheduledPublishAt"]!.GetValue<string>().ShouldBe(later);
        moved.Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);

        (await fixture.DeleteAsync($"page/{id}/schedule/unpublish")).Status.ShouldBe(HttpStatusCode.NoContent);
        var cancelled = Item(await fixture.GetAsync("page?filter[section][eq]=planned"), id);
        cancelled["scheduledPublishAt"]!.GetValue<string>().ShouldBe(later);
        cancelled["scheduledUnpublishAt"].ShouldBeNull();
    }

    // The second publish finds the entry published already, so its job skips it.
    [Fact]
    public async Task AScheduleThatRanOrWasSkippedIsNoLongerShown()
    {
        var ran = await fixture.CreatePageAsync("Runs");
        var skipped = await fixture.CreatePageAsync("Published by hand");
        var at = At(10);
        await ScheduleAsync(ran, "publish", at);
        await ScheduleAsync(skipped, "publish", at);
        (await fixture.PostAsync($"page/{skipped}/publish")).Status.ShouldBe(HttpStatusCode.OK);
        (await fixture.GetAsync($"page/{skipped}")).Json["scheduledPublishAt"]!.GetValue<string>().ShouldBe(at);

        fixture.Host.Time.Advance(TimeSpan.FromMinutes(11));
        (await fixture.RunDueJobsAsync()).ShouldBeGreaterThanOrEqualTo(2);

        var published = await fixture.GetAsync($"page/{ran}");
        published.Json["status"]!.GetValue<string>().ShouldBe("published");
        published.Json["scheduledPublishAt"].ShouldBeNull();
        (await fixture.GetAsync($"page/{ran}?status=published")).Json["scheduledPublishAt"].ShouldBeNull();
        (await fixture.GetAsync($"page/{skipped}")).Json["scheduledPublishAt"].ShouldBeNull();
    }

    [Fact]
    public async Task TheTrashShowsTheScheduleUntilItsJobSkipsIt()
    {
        var id = await fixture.CreatePageAsync("Binned");
        var at = At(10);
        await ScheduleAsync(id, "publish", at);
        (await fixture.DeleteAsync($"page/{id}")).Status.ShouldBe(HttpStatusCode.NoContent);

        var trashed = Item(await fixture.GetAsync("page/trash"), id);
        trashed["scheduledPublishAt"]!.GetValue<string>().ShouldBe(at);
        trashed["deletedAt"].ShouldNotBeNull();

        fixture.Host.Time.Advance(TimeSpan.FromMinutes(11));
        await fixture.RunDueJobsAsync();

        Item(await fixture.GetAsync("page/trash"), id)["scheduledPublishAt"].ShouldBeNull();
        var restored = await fixture.PostAsync($"page/trash/{id}/restore");
        restored.Status.ShouldBe(HttpStatusCode.OK);
        restored.Json["status"]!.GetValue<string>().ShouldBe("draft");
        restored.Json["scheduledPublishAt"].ShouldBeNull();
    }

    // A save skips the scheduled publish of its locale at once (FCMS2409), and its own answer says so.
    [Fact]
    public async Task ASaveThatSkipsAStalePublishAnswersWithoutItAndKeepsTheUnpublish()
    {
        var id = await fixture.CreatePageAsync("Stale");
        var withdrawn = At(20);
        await ScheduleAsync(id, "publish", At(10));
        await ScheduleAsync(id, "unpublish", withdrawn);

        var saved = await ManagementFixture.SendAsync(fixture.Editor, HttpMethod.Put, $"page/{id}", """{ "title": "Moved on" }""", "\"1\"");

        saved.Status.ShouldBe(HttpStatusCode.OK, saved.Body?.ToJsonString());
        saved.Json["scheduledPublishAt"].ShouldBeNull();
        saved.Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);
        var read = await fixture.GetAsync($"page/{id}");
        read.Json["scheduledPublishAt"].ShouldBeNull();
        read.Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);

        var patched = await fixture.PatchAsync($"page/{id}", """{ "title": "Patched" }""", "\"2\"");
        patched.Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);
        var stale = await fixture.PatchAsync($"page/{id}", """{ "title": "Lost" }""", "\"2\"");
        stale.Status.ShouldBe(HttpStatusCode.PreconditionFailed);
        stale.Json["current"]!["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);
    }

    [Fact]
    public async Task EachLocaleCarriesItsOwnSchedules()
    {
        var id = await fixture.CreatePageAsync("English", "locales");
        (await fixture.PatchAsync($"page/{id}?locale=fr", """{ "title": "Français" }""", "\"0\"")).Status.ShouldBe(HttpStatusCode.OK);
        var at = At(10);
        (await fixture.PostAsync($"page/{id}/schedule?locale=fr", $$"""{ "action": "publish", "at": "{{at}}" }""")).Status.ShouldBe(HttpStatusCode.OK);

        (await fixture.GetAsync($"page/{id}")).Json["scheduledPublishAt"].ShouldBeNull();
        (await fixture.GetAsync($"page/{id}?locale=fr")).Json["scheduledPublishAt"]!.GetValue<string>().ShouldBe(at);

        var single = (await fixture.GetAsync($"page/{id}?locale=all")).Json["locales"]!;
        single["en"]!["scheduledPublishAt"].ShouldBeNull();
        single["fr"]!["scheduledPublishAt"]!.GetValue<string>().ShouldBe(at);
        var listed = Item(await fixture.GetAsync("page?locale=all&filter[section][eq]=locales"), id)["locales"]!;
        listed["en"]!["scheduledPublishAt"].ShouldBeNull();
        listed["fr"]!["scheduledPublishAt"]!.GetValue<string>().ShouldBe(at);
    }

    [Fact]
    public async Task DeliveryAnswersNoScheduleAndKnowsNoSuchField()
    {
        var id = await fixture.CreatePageAsync("Public", "public");
        (await fixture.PostAsync($"page/{id}/publish")).Status.ShouldBe(HttpStatusCode.OK);
        var withdrawn = At(10);
        await ScheduleAsync(id, "unpublish", withdrawn);

        var entry = await DeliveryAsync($"page/{id}");
        var listed = Item(await DeliveryAsync("page?filter[section][eq]=public"), id);
        var selected = await DeliveryAsync($"page/{id}?fields=title,scheduledUnpublishAt");

        entry.Status.ShouldBe(HttpStatusCode.OK);
        entry.Json.AsObject().ContainsKey("scheduledPublishAt").ShouldBeFalse();
        entry.Json.AsObject().ContainsKey("scheduledUnpublishAt").ShouldBeFalse();
        listed.AsObject().ContainsKey("scheduledUnpublishAt").ShouldBeFalse();
        selected.Status.ShouldBe(HttpStatusCode.BadRequest);
        selected.Json["code"]!.GetValue<string>().ShouldBe("FCMS2202");
        (await fixture.GetAsync($"page/{id}?status=published")).Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);
    }

    // The fields are read-only: a client that sends back what it read moves no schedule, and they are not filtered or
    // sorted by yet.
    [Fact]
    public async Task AManagementReadSelectsTheSchedulesAndAWriteIgnoresThem()
    {
        var id = await fixture.CreatePageAsync("Selected");
        var withdrawn = At(10);
        await ScheduleAsync(id, "unpublish", withdrawn);

        var selected = await fixture.GetAsync($"page/{id}?fields=title,scheduledUnpublishAt");
        selected.Json.AsObject().Select(pair => pair.Key).ShouldBe(["id", "title", "scheduledUnpublishAt"], ignoreOrder: true);
        selected.Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);

        var read = (await fixture.GetAsync($"page/{id}")).Json.AsObject();
        read["scheduledPublishAt"] = At(5);
        read["scheduledUnpublishAt"] = At(50);
        var echoed = await ManagementFixture.SendAsync(fixture.Editor, HttpMethod.Put, $"page/{id}", read.ToJsonString(), "\"1\"");
        echoed.Status.ShouldBe(HttpStatusCode.OK, echoed.Body?.ToJsonString());
        echoed.Json["scheduledPublishAt"].ShouldBeNull();
        echoed.Json["scheduledUnpublishAt"]!.GetValue<string>().ShouldBe(withdrawn);

        (await fixture.GetAsync($"page?filter[scheduledUnpublishAt][null]=false")).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.GetAsync("page?sort=scheduledUnpublishAt")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    private string At(int minutes) =>
        fixture.Host.Time.GetUtcNow().AddMinutes(minutes).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task ScheduleAsync(string id, string action, string at)
    {
        var scheduled = await fixture.PostAsync($"page/{id}/schedule", $$"""{ "action": "{{action}}", "at": "{{at}}" }""");
        scheduled.Status.ShouldBe(HttpStatusCode.OK, scheduled.Body?.ToJsonString());
    }

    private async Task<ApiResponse> DeliveryAsync(string path)
    {
        using var response = await fixture.Editor.GetAsync(new Uri("/api/delivery/v1/" + path, UriKind.Relative), TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return new ApiResponse(response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text), response.Headers);
    }

    private static JsonNode Item(ApiResponse list, string id) =>
        list.Json["items"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
}
