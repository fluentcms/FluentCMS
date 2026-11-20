using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The bulk actions of a list (A-30): the selected ids sent in chunks of 50, one after another, each with its own key, with
// the progress and the result of each entry as they come; cancel, a chunk that fails as a whole, a selection by filter, the
// actions the caller may take, and a half's action.
public sealed class ContentListBulkTests : AdminTestContext
{
    private const string Bulk = "content/article/bulk";

    private readonly RecordingTime time = new();
    private readonly List<(string Key, JsonObject Body, string Query)> sent = [];

    public ContentListBulkTests()
    {
        UseNeutralResources();
        Services.AddSingleton<TimeProvider>(time);
        Management.SetTypes(MainSite, Type("article", fields: [Field("title", required: true)]), Type("review", fields: [Field("title")]));
        Management.Entries["article"] = [.. Enumerable.Range(1, 120).Select(n => (JsonNode)Entry(n))];
        Management.Entries["review"] = [Entry(1), Entry(2)];
        Management.Respond = (request, path, body) => path == Bulk ? Answer(request, body!) : null;
    }

    // The answer of the server for each entry, by its number; an entry left out succeeds.
    private Dictionary<int, JsonObject> Outcomes { get; } = [];

    // Answers each call before the bulk answer, in turn, while any is left.
    private Queue<Func<HttpResponseMessage>> Failures { get; } = new();

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
        app.FindAll(".bulk-progress-results tbody tr").Select(row => row.QuerySelector("th")!.TextContent).ShouldBe(["Entry 60"]);
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
    public void CancelStopsAfterTheChunkInFlightAndSaysHowManyWereDone()
    {
        var app = Load();
        SelectPage(app);
        Management.Hold = new TaskCompletionSource();

        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (120)?");
        app.WaitForAssertion(() => Management.Calls.Count(call => call.Path == Bulk).ShouldBe(1));
        DialogButton(ProgressDialog(app), "Cancel").Click();
        app.WaitForAssertion(() => app.Find(".bulk-progress").TextContent.ShouldContain("Stopping after the entries being sent now."));
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
        for (var attempt = 0; attempt < BulkAttempts; attempt++)
        {
            Failures.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        var app = Load();
        SelectPage(app);
        RunAndConfirm(app, "Publish", "Publish the selected entries of Article (10)?");

        app.WaitForAssertion(() => app.Find(".bulk-progress-question").TextContent.ShouldContain("Entries 1 to 10 could not be sent"));
        BulkKeys().Count.ShouldBe(BulkAttempts);
        time.Waits.ShouldContain(TimeSpan.FromSeconds(2));
        time.Waits.ShouldContain(TimeSpan.FromSeconds(4));

        DialogButton(ProgressDialog(app), "Try again").Click();

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 10 succeeded, 0 failed."));
        sent.Count.ShouldBe(1);
        BulkKeys().Distinct().Count().ShouldBe(1);
        BulkKeys().Count.ShouldBe(BulkAttempts + 1);
    }

    [Fact]
    public void StoppingAfterAChunkFailedReportsItsEntriesAndRunsNoMore()
    {
        for (var attempt = 0; attempt < BulkAttempts; attempt++)
        {
            Failures.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        var app = Load();
        SelectPage(app);
        RunAndConfirm(app, "Unpublish", "Unpublish the selected entries of Article (120)?");
        app.WaitForAssertion(() => app.Find(".bulk-progress-question"));

        DialogButton(ProgressDialog(app), "Stop").Click();

        app.WaitForAssertion(() => Summary(app).ShouldBe("Stopped: 0 of 120 done, 50 failed."));
        app.Find(".bulk-progress-problem").TextContent.ShouldContain("The run stopped. Entries 1 to 50 could not be sent");
        BulkKeys().Count.ShouldBe(BulkAttempts);
        ResultRow(app, "Entry 51").TextContent.ShouldContain("Not run");
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
    public void ASelectionByFilterShowsTheFilterAndSendsItWithACapAndItsResultAtTheEnd()
    {
        Management.Total = 600;
        var app = Load("content/article?filter[title][startsWith]=Entry");

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 600 that match").Click();
        Choose(MenuItem(app, "Publish"));

        var confirmation = Dialog(app, "Publish");
        confirmation.Find("p").TextContent.ShouldBe("Publish every entry of Article that matches the filter (600)?");
        confirmation.Find("li").TextContent.ShouldBe("Title starts with Entry");
        DialogButton(confirmation, "Publish").Click();

        app.WaitForAssertion(() => Summary(app).ShouldBe("Done: 120 succeeded, 0 failed."));
        var call = sent.Single();
        call.Query.ShouldBe("filter[title][startsWith]=Entry");
        call.Body.ToJsonString().ShouldBe("""{"operation":"publish","cap":600}""");
        ResultRow(app, "Entry 120").TextContent.ShouldContain("Published");
    }

    [Fact]
    public void ASelectionOfEveryEntryWithoutAFilterSendsAFilterEveryEntryMatches()
    {
        Management.Total = 120;
        Management.Entries["article"] = [.. Enumerable.Range(1, 100).Select(n => (JsonNode)Entry(n))];
        var app = Load();

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 120 that match").Click();
        Choose(MenuItem(app, "Unpublish"));
        Dialog(app, "Unpublish").FindAll("p").Select(text => text.TextContent).ShouldContain("No filter: every entry of the type.");
        DialogButton(Dialog(app, "Unpublish"), "Unpublish").Click();

        app.WaitForAssertion(() => sent.Count.ShouldBe(1));
        sent.Single().Query.ShouldBe("filter[id][ne]=00000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public void AllThatMatchASearchOrMoreThanTheCapIsNotActedOn()
    {
        Management.Total = 1001;
        var app = Load();

        SelectPage(app);
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == "Select all 1,001 that match").Click();

        app.Find(".content-list-bulk-note").TextContent.ShouldBe("An action takes at most 1000 entries that match. Narrow the filter, or select rows.");
        app.FindAll(".content-list-selection fluent-menu-item").ShouldBeEmpty();
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
        var reviews = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([reviews], []));
        Services.AddScoped<IAdminAssemblyLoader, SampleLoader>();
        Management.Grant(MainSite, ["schema.read", "reviews.notes.manage"]);
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        var app = Load("content/review");

        SelectPage(app);
        app.WaitForAssertion(() => app.FindAll(".content-list-selection fluent-menu-item").Select(item => item.TextContent.Trim()).ShouldContain("Approve"));
        RunAndConfirm(app, "Approve", "Approve: the selected entries of Review (2)?", title: "Approve: Review");

        app.WaitForAssertion(() => Summary(app, "Approve: Review").ShouldBe("Done: 2 succeeded, 0 failed."));
        ResultRow(app, "Entry 1", "Approve: Review").TextContent.ShouldContain("approved");
        sent.ShouldBeEmpty();
    }

    [Fact]
    public void AHalfsActionIsNotOfferedOnAnotherTypeNorWithoutItsPermission()
    {
        var reviews = AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));
        Services.AddSingleton(new AdminPluginCatalog([reviews], []));
        Services.AddScoped<IAdminAssemblyLoader, SampleLoader>();
        Management.Grant(MainSite, ["schema.read"]);
        Management.Plugins[MainSite] = [Plugin("reviews", enabled: true)];
        var app = Load("content/review");

        SelectPage(app);

        app.FindAll(".content-list-selection fluent-menu-item").Select(item => item.TextContent.Trim()).ShouldBe(["Publish", "Unpublish", "Move to trash"]);
    }

    private const int BulkAttempts = 3;

    private List<string> BulkKeys() => keys;

    private HttpResponseMessage Answer(HttpRequestMessage request, string body)
    {
        var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : "";
        keys.Add(key);
        if (Failures.TryDequeue(out var failure))
        {
            return failure();
        }

        var json = JsonNode.Parse(body)!.AsObject();
        sent.Add((key, json, Uri.UnescapeDataString(request.RequestUri!.Query.TrimStart('?'))));
        IEnumerable<string> ids = json["items"] is JsonArray items
            ? items.Select(item => item!["id"]!.GetValue<string>())
            : Management.Entries["article"].Select(entry => entry!["id"]!.GetValue<string>());
        var answered = ids.Select((id, index) =>
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

    private readonly List<string> keys = [];

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

    private static void SelectPage(IRenderedComponent<AdminApp> app)
    {
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

    private static void CloseProgress(IRenderedComponent<AdminApp> app)
    {
        var dialog = ProgressDialog(app);
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
}
