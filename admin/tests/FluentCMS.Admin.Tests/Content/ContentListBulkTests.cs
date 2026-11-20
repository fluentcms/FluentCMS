using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The bulk actions of a list (A-30): the selected ids sent in chunks of 50, one after another, each with its own key, with
// the progress and the result of each entry as they come; cancel, a chunk that fails as a whole, all that match read as ids,
// the locales an action acts in, the actions the caller may take, and a half's action, also one that throws or hangs.
public sealed class ContentListBulkTests : AdminTestContext
{
    private const string Bulk = "content/article/bulk";
    private const int BulkAttempts = 3;

    private readonly StepTime time = new();
    private readonly List<(string Key, JsonObject Body)> sent = [];
    private readonly List<string> keys = [];
    private readonly List<string> idReads = [];

    public ContentListBulkTests()
    {
        UseNeutralResources();
        Services.AddSingleton<TimeProvider>(time);
        Management.SetTypes(MainSite, Type("article", fields: [Field("title", required: true)]), Type("review", fields: [Field("title")]));
        Management.Entries["article"] = [.. Enumerable.Range(1, 120).Select(n => (JsonNode)Entry(n))];
        Management.Entries["review"] = [Entry(1), Entry(2)];
        Management.Respond = (request, path, body) => path == Bulk ? Answer(request, body!) : IdRead(request, path);
    }

    // The answer of the server for each entry, by its number; an entry left out succeeds.
    private Dictionary<int, JsonObject> Outcomes { get; } = [];

    // Answers each call before the bulk answer, in turn, while any is left.
    private Queue<Func<HttpResponseMessage>> Failures { get; } = new();

    // The entries a read of ids pages through, as the server would for all that match.
    private int Matching { get; set; } = 120;

    // The read of ids, counted from 1, that the server fails, or 0 for none.
    private int FailingPage { get; set; }

    [Fact]
    public void ThePublishOfASelectionGoesInChunksOfFiftyEachWithItsKeyAndShowsEachResult()
    {
        Outcomes[60] = Failed(422, "FCMS1101", "Video URL is required.");
        var app = Load();

        SelectPage(app);
        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (120)?");

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 119 succeeded, 1 failed."));
        sent.Select(call => Ids(call.Body).Count).ShouldBe([50, 50, 20]);
        sent.Select(call => call.Body["operation"]!.GetValue<string>()).ShouldAllBe(operation => operation == "publish");
        sent.Select(call => call.Key).Distinct().Count().ShouldBe(3);
        sent.SelectMany(call => Ids(call.Body)).ShouldBe([.. Enumerable.Range(1, 120).Select(Id)]);
        Progress(app).GetAttribute("value").ShouldBe("120");
        Progress(app).GetAttribute("max").ShouldBe("120");

        var failed = ResultRow(app, "Entry 60");
        failed.TextContent.ShouldContain("Failed");
        failed.TextContent.ShouldContain("Video URL is required.");
        ResultRow(app, "Entry 61").TextContent.ShouldContain("Published");
        ResultRow(app, "Entry 1").TextContent.ShouldContain("Check the related entries.");

        // Progress is announced once per chunk and at the end, not per entry.
        Announced(app).ShouldBe(["50 of 120 done, 0 failed.", "100 of 120 done, 1 failed.", "Done: 119 succeeded, 1 failed."]);

        FailuresOnly(app);
        ProgressDialog(app).FindAll(".bulk-progress-results tbody tr").Select(row => row.QuerySelector("th")!.TextContent).ShouldBe(["Entry 60"]);
    }

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(50, new[] { 50 })]
    [InlineData(51, new[] { 50, 1 })]
    public void TheChunksEndAtFiftyIds(int count, int[] chunks)
    {
        Management.Entries["article"] = [.. Enumerable.Range(1, count).Select(n => (JsonNode)Entry(n))];
        var app = Load();

        SelectPage(app);
        RunAndConfirm(app, "Publish", $"Publish the selected entries of Article ({count})?");

        app.WaitForAssertion(() => Summary(app).ShouldBe($"Done: {count} succeeded, 0 failed."));
        sent.Select(call => Ids(call.Body).Count).ShouldBe(chunks);
    }

    [Fact]
    public async Task NoIdsSendNothing()
    {
        var app = Load();
        var progress = Services.GetRequiredService<BulkProgressService>().Begin("Nothing", 0, chunked: true);

        await app.InvokeAsync(() => Services.GetRequiredService<BulkRunner>().RunAsync("article", BulkOperation.Publish, [], null, null, progress));

        keys.ShouldBeEmpty();
        progress.Results.ShouldBeEmpty();
    }

    [Fact]
    public void AfterTheRunTheListIsReadAgainWithItsQueryAndTheSelectionKeepsTheFailedEntries()
    {
        Outcomes[3] = Failed(422, "FCMS1101", "Video URL is required.");
        var app = Load("content/article?sort=title");
        var reads = Management.ContentQueries.Count;

        SelectPage(app);
        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (120)?");
        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 119 succeeded, 1 failed."));
        CloseProgress(app);

        app.WaitForAssertion(() => Management.ContentQueries.Count.ShouldBe(reads + 1));
        Management.ContentQueries[^1].ShouldStartWith("sort=title&");
        app.WaitForAssertion(() => app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page."));
        app.FindAll("tbody tr[aria-selected=true]").Single().TextContent.ShouldContain("Entry 3");
        app.FindAll(".bulk-progress").ShouldBeEmpty();
        Document.Invocations.Count(call => call.Identifier == "focusHeading").ShouldBeGreaterThan(0);
    }

    [Fact]
    public void TheConfirmationPutsCancelFirstAndFocusesIt()
    {
        var app = Load();
        SelectPage(app);
        var focuses = Focuses();

        Choose(MenuItem(app, "Move to trash"));

        var confirmation = Dialog(app, "Move to trash");
        confirmation.FindAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Cancel", "Move to trash"]);

        // The focus follows the library's own on a timer and renders nothing, so its call is waited for.
        SpinWait.SpinUntil(() => Focuses() > focuses, TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }

    [Fact]
    public void ALocalizedTypeNamesTheLocaleOfAPublishAndEveryLocaleForADelete()
    {
        var article = Type("article", fields: [Field("title", required: true)]);
        article["localized"] = true;
        Management.SetTypes(MainSite, article);
        var app = Load();
        SelectPage(app);

        Choose(MenuItem(app, "Unpublish"));
        Dialog(app, "Unpublish").FindAll("p").Select(text => text.TextContent).ShouldContain("In en only. The other locales stay as they are.");
        DialogButton(Dialog(app, "Unpublish"), "Cancel").Click();

        Choose(MenuItem(app, "Move to trash"));
        Dialog(app, "Move to trash").FindAll("p").Select(text => text.TextContent).ShouldContain("In every locale: each entry goes to the trash with all its translations.");
    }

    [Fact]
    public void CancelStopsAfterTheChunkInFlightAndSaysHowManyWereDone()
    {
        var app = Load();
        SelectPage(app);
        Management.Hold = new TaskCompletionSource();

        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (120)?");
        app.WaitForAssertion(() => Management.Calls.Count(call => call.Path == Bulk).ShouldBe(1));
        DialogButton(ProgressDialog(app), "Cancel").Click();
        app.WaitForAssertion(() => app.Find(".bulk-progress").TextContent.ShouldContain("Stopping after the entries being sent now."));

        // The list's own run waits for its chunk in flight however long it takes: no grace ends it before.
        time.GraceWaiting.ShouldBeFalse();
        ProgressDialog(app).FindAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldNotContain("Close");
        Management.Hold.SetResult();
        Management.Hold = null;

        app.WaitForAssertion(() => ProgressDialog(app).Markup.ShouldContain("bulk-progress-summary"), TimeSpan.FromSeconds(5));
        Summary(app).ShouldBe("Stopped: 50 of 120 done, 0 failed.");
        sent.Count.ShouldBe(1);
        ResultRow(app, "Entry 51").TextContent.ShouldContain("Not run");
        ResultRow(app, "Entry 50").TextContent.ShouldContain("Published");
    }

    [Fact]
    public void AChunkThatFailsAsAWholeIsSentAgainWithTheSameKeyThenTheUserChoosesToTryAgain()
    {
        Management.Entries["article"] = [.. Enumerable.Range(1, 10).Select(n => (JsonNode)Entry(n))];
        Failures.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(7)) } });
        Failures.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        Failures.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var app = Load();
        SelectPage(app);
        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (10)?");

        app.WaitForAssertion(() => app.Find(".bulk-progress-question").TextContent.ShouldContain("Entries 1 to 10 could not be sent"));
        keys.Count.ShouldBe(BulkAttempts);

        // The first wait is the one the answer asked for, longer than the 2 s of the backoff; the second is the backoff.
        time.Waits.ShouldContain(TimeSpan.FromSeconds(7));
        time.Waits.ShouldContain(TimeSpan.FromSeconds(4));

        DialogButton(ProgressDialog(app), "Try again").Click();

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 10 succeeded, 0 failed."));
        sent.Count.ShouldBe(1);
        keys.Distinct().Count().ShouldBe(1);
        keys.Count.ShouldBe(BulkAttempts + 1);
    }

    [Fact]
    public void AChunkLeftUnansweredIsResentWithItsKeyAndIsNotConfirmedWhenTheUserStops()
    {
        for (var attempt = 0; attempt < BulkAttempts; attempt++)
        {
            Failures.Enqueue(() => throw new HttpRequestException("The connection was reset."));
        }

        var app = Load();
        SelectPage(app);
        RunAndConfirm(app, "Unpublish", "Unpublish the selected entries of Article (120)?");
        app.WaitForAssertion(() => app.Find(".bulk-progress-question"));
        keys.Count.ShouldBe(BulkAttempts);
        keys.Distinct().Count().ShouldBe(1);

        DialogButton(ProgressDialog(app), "Stop").Click();

        app.WaitForAssertion(() => Summary(app).ShouldBe("Stopped: 0 of 120 done, 0 failed. 50 were sent without an answer and may have been done; check them before acting on them again."));
        app.Find(".bulk-progress-problem").TextContent.ShouldContain("The run stopped. Entries 1 to 50 could not be sent");
        ResultRow(app, "Entry 1").TextContent.ShouldContain("Not confirmed");
        ResultRow(app, "Entry 51").TextContent.ShouldContain("Not run");
        CloseProgress(app);
        app.WaitForAssertion(() => app.Find(".content-list-selection").TextContent.ShouldContain("120 selected on this page."));
    }

    [Fact]
    public void AChunkThatTimesOutIsResentWithTheSameKey()
    {
        Management.Entries["article"] = [.. Enumerable.Range(1, 10).Select(n => (JsonNode)Entry(n))];
        Failures.Enqueue(() => throw new TaskCanceledException("The attempt timed out.", new TimeoutException()));
        var app = Load();

        SelectPage(app);
        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (10)?");

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 10 succeeded, 0 failed."));
        keys.Count.ShouldBe(2);
        keys.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public void AnEntryTheCallerMayNotChangeShowsAsRefusedAndTheRunGoesOn()
    {
        Outcomes[2] = Failed(403, "FCMS2001", "Your roles do not let you delete this entry.");
        Management.Entries["article"] = [Entry(1), Entry(2), Entry(3)];
        var app = Load();

        SelectPage(app);
        RunAndConfirm(app, "Move to trash", "Move the selected entries of Article (3) to the trash?");

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 2 succeeded, 1 failed."));
        sent.Single().Body["operation"]!.GetValue<string>().ShouldBe("delete");
        ResultRow(app, "Entry 2").TextContent.ShouldContain("Refused");
        ResultRow(app, "Entry 3").TextContent.ShouldContain("Moved to trash");
    }

    [Fact]
    public void AllThatMatchAreReadAsIdsByTheCursorAndCutAtAThousandWithTheFilterShown()
    {
        Matching = 1001;
        Management.Total = 1001;
        var app = Load("content/article?filter[title][startsWith]=Entry");

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 1,001 that match").Click();
        Choose(MenuItem(app, "Publish"));

        var confirmation = Dialog(app, "Publish");
        app.WaitForAssertion(() => confirmation.Find("p").TextContent.ShouldBe("Publish the entries of Article that match (1,000)?"));
        confirmation.FindAll("p").Select(text => text.TextContent).ShouldContain("More than 1,000 entries match. Only the first 1,000, in the list's order, are acted on: an action takes at most 1,000.");
        confirmation.FindAll("p").Select(text => text.TextContent).ShouldContain("These are the entries that match now: one that stops matching before its turn is still acted on.");
        confirmation.Find("li").TextContent.ShouldBe("Title starts with Entry");
        idReads.Count.ShouldBe(11);
        idReads.ShouldAllBe(query => query.Contains("filter[title][startsWith]=Entry", StringComparison.Ordinal) && query.Contains("pageSize=100", StringComparison.Ordinal) && !query.Contains("page=", StringComparison.Ordinal));
        idReads[0].ShouldNotContain("cursor=");
        idReads[^1].ShouldContain("cursor=1000");

        DialogButton(confirmation, "Cancel").Click();
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void AFailedPageOfTheReadOfAllThatMatchSaysSoAndRunsNothing()
    {
        Matching = 250;
        Management.Total = 250;
        FailingPage = 2;
        var app = Load();

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 250 that match").Click();
        Choose(MenuItem(app, "Publish"));

        app.WaitForAssertion(() => Dialog(app, "Publish").Find("p").TextContent.ShouldBe("The entries that match could not be read. Close this and try again."));
        DialogButton(Dialog(app, "Publish"), "Publish").HasAttribute("disabled").ShouldBeTrue();
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void AllThatMatchASearchAreReadWithTheSearchAndItIsShown()
    {
        var title = Field("title", required: true);
        title["searchable"] = true;
        Management.SetTypes(MainSite, Type("article", fields: [title]));
        Matching = 130;
        Management.Total = 130;
        var app = Load("content/article?q=entry");

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 130 that match").Click();
        Choose(MenuItem(app, "Publish"));

        app.WaitForAssertion(() => Dialog(app, "Publish").Find("li").TextContent.ShouldBe("Search: entry"));
        idReads.ShouldAllBe(query => query.Contains("q=entry", StringComparison.Ordinal));
        DialogButton(Dialog(app, "Publish"), "Publish").Click();
        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 130 succeeded, 0 failed."));
        sent.Select(call => Ids(call.Body).Count).ShouldBe([50, 50, 30]);
    }

    [Fact]
    public void AllThatMatchWithoutAFilterSaysSo()
    {
        Matching = 130;
        Management.Total = 130;
        var app = Load();

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 130 that match").Click();
        Choose(MenuItem(app, "Unpublish"));

        app.WaitForAssertion(() => Dialog(app, "Unpublish").FindAll("p").Select(text => text.TextContent).ShouldContain("No filter: every entry of the type."));
        Dialog(app, "Unpublish").Find("p").TextContent.ShouldBe("Unpublish the entries of Article that match (130)?");
        DialogButton(Dialog(app, "Unpublish"), "Unpublish").Click();

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 130 succeeded, 0 failed."));
        sent.Select(call => Ids(call.Body).Count).ShouldBe([50, 50, 30]);
    }

    [Fact]
    public void TheSelectionIsClearedByAnotherFilterLocaleOrType()
    {
        var article = Type("article", fields: [Field("title", required: true)]);
        article["localized"] = true;
        Management.SetTypes(MainSite, article, Type("review", fields: [Field("title")]));
        Management.AddLocale(MainSite, "fr", 1);
        var app = Load();
        var navigation = Services.GetRequiredService<NavigationManager>();

        SelectPage(app);
        navigation.NavigateTo("content/article?filter[title][startsWith]=Entry");
        app.WaitForAssertion(() => app.FindAll(".content-list-selection").ShouldBeEmpty());

        SelectPage(app);
        navigation.NavigateTo("content/article?filter[title][startsWith]=Entry&locale=fr");
        app.WaitForAssertion(() => app.FindAll(".content-list-selection").ShouldBeEmpty());

        SelectPage(app);
        navigation.NavigateTo("content/review");
        app.WaitForAssertion(() => app.FindAll(".content-list-selection").ShouldBeEmpty());
    }

    [Fact]
    public void ActionsTheCallerMayNotTakeAreNotOffered()
    {
        var access = Management.Access[MainSite]["types"]!["article"]!;
        access["publish"] = false;
        access["delete"] = false;
        var app = Load();

        SelectPage(app);

        app.FindAll(".content-list-selection fluent-menu-item").Select(item => item.TextContent.Trim()).ShouldBe(["Unpublish"]);
    }

    [Fact]
    public void ACallerWhoMayTakeNoActionSeesNoMenu()
    {
        var access = Management.Access[MainSite]["types"]!["article"]!;
        access["publish"] = false;
        access["unpublish"] = false;
        access["delete"] = false;
        var app = Load();

        SelectPage(app);

        app.FindAll(".content-list-selection fluent-menu").ShouldBeEmpty();
    }

    [Fact]
    public void ABulkActionOfAHalfShowsInTheMenuOfItsTypeRunsAndReportsItsProgress()
    {
        var app = LoadReviews(Reviews, permitted: true);

        SelectPage(app);
        app.WaitForAssertion(() => MenuItems(app).ShouldContain("Approve"));
        RunAndConfirm(app, "Approve", "Approve: the selected entries of Review (2)?", title: "Approve: Review");

        app.WaitForAssertion(() => Summary(app, "Approve: Review").ShouldBe("Done: 2 succeeded, 0 failed."));
        ResultRow(app, "Entry 1", "Approve: Review").TextContent.ShouldContain("approved");
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void AHalfsActionIsNotOfferedOnAnotherTypeNorWithoutItsPermission()
    {
        var app = LoadReviews(Reviews, permitted: false);

        SelectPage(app);

        MenuItems(app).ShouldBe(["Publish", "Unpublish", "Move to trash"]);
    }

    [Fact]
    public void AHalfsActionThatThrowsEndsWithItsProblemAndTheListGoesOn()
    {
        var app = LoadReviews(Reviews with { BulkActions = [Action("ThrowingBulkAction", "throw")] }, permitted: true);
        SelectPage(app);
        app.WaitForAssertion(() => MenuItems(app).ShouldContain("Throw"));

        RunAndConfirm(app, "Throw", "Throw: the selected entries of Review (2)?", title: "Throw: Review");

        app.WaitForAssertion(() => Summary(app, "Throw: Review").ShouldBe("Stopped: 1 of 2 done, 0 failed."));
        ProgressDialog(app, "Throw: Review").Find(".bulk-progress-problem").TextContent.ShouldBe("The action failed. The browser's console has the cause.");
        DialogButton(ProgressDialog(app, "Throw: Review"), "Close");
    }

    [Fact]
    public void AHalfsActionThatIgnoresTheCancelIsEndedAfterTheGraceAndTheDialogCanBeClosed()
    {
        var app = LoadReviews(Reviews with { BulkActions = [Action("HangingBulkAction", "hang")] }, permitted: true);
        SelectPage(app);
        app.WaitForAssertion(() => MenuItems(app).ShouldContain("Hang"));
        RunAndConfirm(app, "Hang", "Hang: the selected entries of Review (2)?", title: "Hang: Review");
        app.WaitForAssertion(() => ResultRow(app, "Entry 1", "Hang: Review"));

        DialogButton(ProgressDialog(app, "Hang: Review"), "Cancel").Click();
        app.WaitForAssertion(() => time.GraceWaiting.ShouldBeTrue());
        ProgressDialog(app, "Hang: Review").FindAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldNotContain("Close");
        time.EndGrace();

        app.WaitForAssertion(() => ProgressDialog(app, "Hang: Review").Find(".bulk-progress-problem").TextContent.ShouldStartWith("The action did not stop within 10 seconds"));
        var reads = Management.ContentQueries.Count;
        CloseProgress(app, "Hang: Review");
        app.WaitForAssertion(() => Management.ContentQueries.Count.ShouldBe(reads + 1));
        app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page.");
    }

    [Fact]
    public void AFailureAfterTheGraceEndedTheRunIsDropped()
    {
        LateBulkAction.Outcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = LoadReviews(Reviews with { BulkActions = [Action("LateBulkAction", "late")] }, permitted: true);
        RunLateAndEndTheGrace(app);

        LateBulkAction.Outcome.SetResult(false);

        app.WaitForAssertion(() => ProgressDialog(app, "Late: Review").FindAll(".bulk-progress-problem").Single().TextContent.ShouldStartWith("The action did not stop within 10 seconds"));
        ProgressDialog(app, "Late: Review").FindAll(".bulk-progress-results tbody tr").Count.ShouldBe(1);
        CloseProgress(app, "Late: Review");
        app.WaitForAssertion(() => app.FindAll(".bulk-progress").ShouldBeEmpty());
    }

    [Fact]
    public void ASuccessAfterTheDialogClosedIsDropped()
    {
        LateBulkAction.Outcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = LoadReviews(Reviews with { BulkActions = [Action("LateBulkAction", "late")] }, permitted: true);
        RunLateAndEndTheGrace(app);
        CloseProgress(app, "Late: Review");
        app.WaitForAssertion(() => app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page."));

        LateBulkAction.Outcome.SetResult(true);

        app.WaitForAssertion(() => app.FindAll(".bulk-progress").ShouldBeEmpty());
        app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page.");
    }

    [Fact]
    public void AFailureAfterTheDialogClosedIsDropped()
    {
        LateBulkAction.Outcome = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = LoadReviews(Reviews with { BulkActions = [Action("LateBulkAction", "late")] }, permitted: true);
        RunLateAndEndTheGrace(app);
        CloseProgress(app, "Late: Review");
        app.WaitForAssertion(() => app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page."));

        LateBulkAction.Outcome.SetResult(false);

        app.WaitForAssertion(() => app.FindAll(".bulk-progress").ShouldBeEmpty());
        app.Find(".content-list-selection").TextContent.ShouldContain("1 selected on this page.");
    }

    private void RunLateAndEndTheGrace(IRenderedComponent<AdminApp> app)
    {
        SelectPage(app);
        app.WaitForAssertion(() => MenuItems(app).ShouldContain("Late"));
        RunAndConfirm(app, "Late", "Late: the selected entries of Review (2)?", title: "Late: Review");
        app.WaitForAssertion(() => ResultRow(app, "Entry 1", "Late: Review"));
        DialogButton(ProgressDialog(app, "Late: Review"), "Cancel").Click();
        app.WaitForAssertion(() => time.GraceWaiting.ShouldBeTrue());
        time.EndGrace();
        app.WaitForAssertion(() => DialogButton(ProgressDialog(app, "Late: Review"), "Close"));
    }

    private static AdminPluginManifest Reviews => AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));

    private static AdminManifestAction Action(string type, string name) =>
        new() { Type = $"FluentCMS.Admin.Plugins.Sample.{type}", Name = name, ContentTypes = ["review"] };

    private IRenderedComponent<AdminApp> LoadReviews(AdminPluginManifest half, bool permitted)
    {
        Services.AddSingleton(new AdminPluginCatalog([half], []));
        Services.AddScoped<IAdminAssemblyLoader, SampleLoader>();
        Management.Grant(MainSite, permitted ? ["schema.read", "reviews.notes.manage"] : ["schema.read"]);
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        return Load("content/review");
    }

    private HttpResponseMessage Answer(HttpRequestMessage request, string body)
    {
        var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : "";
        keys.Add(key);
        if (Failures.TryDequeue(out var failure))
        {
            return failure();
        }

        var json = JsonNode.Parse(body)!.AsObject();
        sent.Add((key, json));
        var answered = Ids(json).Select((id, index) =>
        {
            var n = int.Parse(id[^12..], System.Globalization.CultureInfo.InvariantCulture);
            var item = Outcomes.TryGetValue(n, out var outcome)
                ? (JsonObject)outcome.DeepClone()
                : new JsonObject { ["status"] = "succeeded", ["version"] = 2 };
            item["index"] = index;
            item["id"] = id;
            if (n == 1 && item["status"]!.GetValue<string>() == "succeeded")
            {
                item["warnings"] = new JsonArray(new JsonObject { ["path"] = "author", ["code"] = "FCMS1450", ["message"] = "Check the related entries." });
            }

            return (JsonNode)item;
        });
        return Json(new JsonObject { ["items"] = new JsonArray([.. answered]), ["cancelled"] = false });
    }

    // A read of the ids of all that match, by the cursor, which here is the number of entries read before; FailingPage fails
    // that page of the read.
    private HttpResponseMessage? IdRead(HttpRequestMessage request, string path)
    {
        var query = Uri.UnescapeDataString(request.RequestUri!.Query.TrimStart('?'));
        if (request.Method != HttpMethod.Get || path != "content/article" || !query.Contains("fields=id,title&", StringComparison.Ordinal))
        {
            return null;
        }

        idReads.Add(query);
        var cursor = query.Split('&').FirstOrDefault(pair => pair.StartsWith("cursor=", StringComparison.Ordinal));
        var from = cursor is null ? 0 : int.Parse(cursor["cursor=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        if (FailingPage == idReads.Count)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var count = Math.Max(0, Math.Min(100, Matching - from));
        var rows = Enumerable.Range(from + 1, count).Select(n => (JsonNode)Entry(n));
        return Json(new JsonObject
        {
            ["items"] = new JsonArray([.. rows]),
            ["pageSize"] = 100,
            ["nextCursor"] = from + count < Matching ? (from + count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        });
    }

    private static JsonObject Failed(int status, string code, string detail) => new()
    {
        ["status"] = "failed",
        ["problem"] = new JsonObject { ["status"] = status, ["code"] = code, ["title"] = "Problem", ["detail"] = detail },
    };

    private static string Id(int n) => $"0193a1b2-0000-7000-8000-{n:D12}";

    private static JsonObject Entry(int n) => new()
    {
        ["id"] = Id(n),
        ["status"] = "draft",
        ["locale"] = "en",
        ["version"] = 1,
        ["updatedAt"] = "2026-11-01T10:00:00Z",
        ["title"] = $"Entry {n}",
    };

    private static List<string> Ids(JsonObject body) => [.. body["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>())];

    private int Focuses() => JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");

    private static void SelectPage(IRenderedComponent<AdminApp> app)
    {
        app.WaitForAssertion(() => app.FindAll(".cms-data-grid tbody tr input[type=checkbox]").Count.ShouldBeGreaterThan(0));
        app.Find("thead button[role=checkbox]").Click();
        app.WaitForAssertion(() => app.Find(".content-list-selection"));
    }

    private static void RunAndConfirm(IRenderedComponent<AdminApp> app, string action, string question, string? title = null)
    {
        app.WaitForAssertion(() => MenuItem(app, action));
        Choose(MenuItem(app, action));
        var confirmation = Dialog(app, action);
        confirmation.Find("p").TextContent.ShouldBe(question);
        DialogButton(confirmation, action).Click();
        app.WaitForAssertion(() => ProgressDialog(app, title ?? $"{action}: Article"));
    }

    private static List<string> MenuItems(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-list-selection fluent-menu-item").Select(item => item.TextContent.Trim())];

    private static IElement MenuItem(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll(".content-list-selection fluent-menu-item").Single(item => item.TextContent.Trim() == text);

    private static void Choose(IElement item) => item.TriggerEvent("onmenuitemchange", new MenuItemEventArgs { Id = item.Id });

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    private static IRenderedComponent<CmsDialog> ProgressDialog(IRenderedComponent<AdminApp> app, string? title = null) =>
        app.FindComponents<CmsDialog>().Single(found => title is null ? found.Instance.Title.EndsWith(": Article", StringComparison.Ordinal) : found.Instance.Title == title);

    private static IElement DialogButton(IRenderedComponent<CmsDialog> dialog, string text) =>
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IElement Progress(IRenderedComponent<AdminApp> app) => app.Find(".bulk-progress progress");

    private static string Summary(IRenderedComponent<AdminApp> app, string? title = null) =>
        ProgressDialog(app, title).Find(".bulk-progress-summary").TextContent;

    private static IElement ResultRow(IRenderedComponent<AdminApp> app, string entry, string? title = null) =>
        ProgressDialog(app, title).FindAll(".bulk-progress-results tbody tr").Single(row => row.QuerySelector("th")!.TextContent == entry);

    private static void FailuresOnly(IRenderedComponent<AdminApp> app)
    {
        var box = app.FindComponents<CmsCheckbox>().Single(found => found.Instance.Label == "Show failures only");
        box.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(true)).GetAwaiter().GetResult();
    }

    private static void CloseProgress(IRenderedComponent<AdminApp> app, string? title = null)
    {
        var dialog = ProgressDialog(app, title);
        DialogButton(dialog, "Close").Click();
        var host = dialog.Find("fluent-dialog");
        host.TriggerEvent("ondialogtoggle", new DialogToggleEventArgs { Id = host.Id, OldState = "open", NewState = "closed" });
    }

    private static List<string> Announced(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[role=status] > div").Select(message => message.TextContent).Where(text => text.Contains(" of ", StringComparison.Ordinal) || text.StartsWith("Done", StringComparison.Ordinal))];

    private IRenderedComponent<AdminApp> Load(string route = "content/article")
    {
        Start();
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        app.WaitForAssertion(() => app.FindAll(".cms-data-grid tbody tr input[type=checkbox]").Count.ShouldBeGreaterThan(0));
        return app;
    }

    private sealed class SampleLoader : IAdminAssemblyLoader
    {
        public Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken) => Task.FromResult(typeof(ReviewsAdminPlugin).Assembly);
    }

    // Lets every wait pass at once and records it, except the grace after a cancel, which passes only when the test ends it.
    private sealed class StepTime : TimeProvider
    {
        private (TimerCallback Callback, object? State)? _grace;

        public List<TimeSpan> Waits { get; } = [];

        public bool GraceWaiting => _grace is not null;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == BulkProgress.StopGrace)
            {
                _grace = (callback, state);
                return new Timer();
            }

            if (dueTime != Timeout.InfiniteTimeSpan)
            {
                Waits.Add(dueTime);
                ThreadPool.QueueUserWorkItem(_ => callback(state));
            }

            return new Timer();
        }

        public void EndGrace()
        {
            var (callback, state) = _grace.ShouldNotBeNull();
            callback(state);
        }

        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
