using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The version history of A-28: the versions newest first in pages, with author by kind and name, time and origin; two versions
// compared field by field, with a block named by its type and position, a moved block and rich text by its words, and no
// field the schema leaves out; a restore after a question that names the version, sent with the draft's tag, and its 412.
public sealed class VersionHistoryTests : AdminTestContext
{
    private readonly FakeContent _content;

    public VersionHistoryTests()
    {
        UseNeutralResources();
        Document.SetupModule(invocation => invocation.Identifier == "rememberFocus");
        Document.SetupVoid("returnFocus", _ => true).SetVoidResult();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Type("post", fields: [Field("title"), Blocks("sections", "hero", "quote"), Field("body", "richText"), Relation("author", "author")]), Type("author", fields: [Field("name")]));
        Management.Schemas[MainSite]["blocks"] = new JsonArray(
            Type("hero", "block", fields: [Field("heading")]),
            Type("quote", "block", fields: [Field("text")]));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheVersionsAreListedNewestFirstInPagesWithAuthorTimeAndOrigin()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, version: 25);
        for (var number = 1; number <= 25; number++)
        {
            _content.AddVersion(id, number, new JsonObject { ["title"] = $"Title {number}" }, authorName: number == 1 ? "" : "Ada", authorKind: number == 1 ? "system" : "adminUser", origin: number == 1 ? "seed" : "admin");
        }

        var app = Load($"content/post/{id:D}");
        var panel = OpenHistory(app);

        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(20));
        _content.Reads.ShouldContain($"/api/management/v1/content/post/{id:D}/versions");
        var newest = Cells(Rows(panel)[0]);
        (newest[0], newest[1], newest[3], newest[4]).ShouldBe(("Version 25", "Ada (user)", "Admin", ""));
        Rows(panel)[1].QuerySelector("fluent-button")!.TextContent.Trim().ShouldBe("Restore");
        Rows(panel)[0].QuerySelector("time")!.GetAttribute("datetime").ShouldBe("2026-11-01T09:25:00.0000000+00:00");

        Button(panel, "Show 5 more").Click();

        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(25));
        Cells(Rows(panel)[24])[..2].ShouldBe(["Version 1", "system"]);
        Cells(Rows(panel)[24])[3].ShouldBe("Seed");
        panel.FindAll("fluent-button").Select(button => button.TextContent.Trim()).ShouldNotContain("Show 5 more");
    }

    [Fact]
    public void TwoVersionsAreComparedByTheirChangedFieldsBlocksAndWords()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "After" }, version: 2);
        JsonObject Hero(string heading) => new() { ["type"] = "hero", ["id"] = "h1", ["heading"] = heading };
        JsonObject Quote() => new() { ["type"] = "quote", ["id"] = "q1", ["text"] = "Be brief" };
        JsonObject Body(string text) => new() { ["type"] = "doc", ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) }) };
        _content.AddVersion(id, 1, new JsonObject { ["title"] = "Before", ["sections"] = new JsonArray(Hero("Welcome"), Quote()), ["body"] = Body("The quick fox") });
        _content.AddVersion(id, 2, new JsonObject { ["title"] = "After", ["sections"] = new JsonArray(Quote(), Hero("Welcome")), ["body"] = Body("The slow fox") });
        _content.Diffs[("en", 1, 2)] = new JsonArray(
            Change("title", "changed", "Before", "After"),
            Change("sections[1]", "added", null, Hero("Welcome")),
            Change("sections[0]", "removed", Hero("Welcome"), null),
            Change("body.content[0].content[0].text", "changed", "The quick fox", "The slow fox"),
            Change("secret", "changed", "hidden before", "hidden after"));

        var app = Load($"content/post/{id:D}");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
        Button(panel, "Compare").Click();

        panel.WaitForAssertion(() => panel.Find(".version-diff h3").TextContent.ShouldBe("Changes from version 1 to version 2"));
        panel.FindAll(".version-diff-table").Select(table => table.GetAttribute("data-field")).ShouldBe(["title", "sections", "body"]);
        DiffRows(panel, "title").ShouldBe([["Title", "Changed", "Before", "After"]]);
        DiffRows(panel, "sections").ShouldBe([["Hero 2", "Moved from position 1 to 2", "", ""]]);
        DiffRows(panel, "body").ShouldBe([["Paragraph 1", "Changed", "The removed: quick fox", "The added: slow fox"]]);
        panel.Find("[data-field='body'] del").TextContent.ShouldBe("removed: quick ");
        panel.Find("[data-field='body'] ins").TextContent.ShouldBe("added: slow ");
        panel.Markup.ShouldNotContain("hidden after");
        panel.FindAll(".version-diff-table th[scope='col']").Take(4).Select(cell => cell.TextContent).ShouldBe(["Where", "Change", "Version 1", "Version 2"]);
    }

    [Fact]
    public void RestoreAsksNamingTheVersionAndMakesANewDraftOnTheTagOfTheRead()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Now" }, version: 2);
        _content.AddVersion(id, 1, new JsonObject { ["title"] = "First" });
        _content.AddVersion(id, 2, new JsonObject { ["title"] = "Now" });
        var app = Load($"content/post/{id:D}");
        app.Find("[data-field='title'] input").Input("Unsaved");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
        Rows(panel)[0].QuerySelector("fluent-button").ShouldBeNull();

        Rows(panel)[1].QuerySelector("fluent-button")!.Click();

        var ask = Dialog(app, "Restore Version 1?");
        app.WaitForAssertion(() => ask.Instance.Open.ShouldBeTrue());
        ask.Find("p").TextContent.ShouldStartWith("A new draft is made from Version 1, saved by Ada (user) on ");
        ask.FindAll("p strong").ShouldHaveSingleItem().TextContent.ShouldBe("Your unsaved changes in the form are replaced.");
        _content.Writes.ShouldBeEmpty();
        ask.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Restore Version 1").Click();

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var restore = _content.Writes[0];
        (restore.Method, restore.Path, restore.IfMatch).ShouldBe((HttpMethod.Post, $"content/post/{id:D}/versions/1/restore", "\"2\""));
        app.WaitForAssertion(() => app.Find("[data-field='title'] input").GetAttribute("value").ShouldBe("First"));
        app.Find(".content-form-saved").TextContent.ShouldBe("Restored version 1 as the draft.");
        _content.Version(id).ShouldBe(3);
        Panel(app).Instance.Open.ShouldBeFalse();
    }

    [Fact]
    public void ARestoreOverADraftChangedElsewhereShowsTheConflict()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Now" }, version: 2);
        _content.AddVersion(id, 1, new JsonObject { ["title"] = "First" });
        _content.AddVersion(id, 2, new JsonObject { ["title"] = "Now" });
        var app = Load($"content/post/{id:D}");
        _content.ChangeElsewhere(id, "title", "Theirs");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
        Rows(panel)[1].QuerySelector("fluent-button")!.Click();
        var ask = Dialog(app, "Restore Version 1?");
        app.WaitForAssertion(() => ask.Instance.Open.ShouldBeTrue());
        ask.FindAll("p strong").ShouldBeEmpty();

        ask.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Restore Version 1").Click();

        app.WaitForAssertion(() => app.Find(".content-form-problem").TextContent.ShouldContain("The entry was changed"));
        app.FindAll(".content-form-problem fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Reload the entry"]);
        _content.Writes.ShouldHaveSingleItem().IfMatch.ShouldBe("\"2\"");
        app.Find("[data-field='title'] input").GetAttribute("value").ShouldBe("Now");
    }

    [Fact]
    public async Task KeepingTheDraftOpensThePanelAgainAndEscapeClosesItWithTheFormUnchanged()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Now" }, version: 2);
        _content.AddVersion(id, 1, new JsonObject { ["title"] = "First" });
        _content.AddVersion(id, 2, new JsonObject { ["title"] = "Now" });
        var app = Load($"content/post/{id:D}");
        app.Find("[data-field='title'] input").Input("Unsaved");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
        Rows(panel)[1].QuerySelector("fluent-button")!.Click();
        var ask = Dialog(app, "Restore Version 1?");
        app.WaitForAssertion(() => ask.Instance.Open.ShouldBeTrue());

        ask.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Keep the draft").Click();

        app.WaitForAssertion(() => Panel(app).Instance.Open.ShouldBeTrue());
        ask.Instance.Open.ShouldBeFalse();
        var drawer = Panel(app);
        await drawer.InvokeAsync(() => drawer.Instance.OpenChanged.InvokeAsync(false));

        app.WaitForAssertion(() => Document.VerifyInvoke("returnFocus"));
        Panel(app).Instance.Open.ShouldBeFalse();
        app.Find("[data-field='title'] input").GetAttribute("value").ShouldBe("Unsaved");
        _content.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void AFailedReadOfTheVersionsIsShownAndTriedAgain()
    {
        var id = TwoVersions();
        Management.Answer = (method, path) => path.EndsWith("/versions", StringComparison.Ordinal) ? Problem(System.Net.HttpStatusCode.InternalServerError, "FCMS1000") : null;
        var app = Load($"content/post/{id:D}");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => panel.Find(".version-panel p").TextContent.ShouldBe("The versions could not be read."));

        Management.Answer = null;
        Button(panel, "Try again").Click();

        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
    }

    [Fact]
    public async Task AReadThatEndsAfterThePanelClosedIsDropped()
    {
        var id = TwoVersions();
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.EndsWith("/versions", StringComparison.Ordinal) ? held.Task : null;
        var app = Load($"content/post/{id:D}");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => panel.Find(".version-panel p[role='status']"));
        await panel.InvokeAsync(() => panel.Instance.OpenChanged.InvokeAsync(false));
        app.WaitForAssertion(() => Panel(app).Instance.Open.ShouldBeFalse());

        held.SetException(new HttpRequestException("late"));
        Management.HoldPath = null;
        panel = OpenHistory(app);

        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
        panel.FindAll(".version-panel > p").ShouldBeEmpty();
    }

    [Fact]
    public void ASlowCompareIsOvertakenByTheNextOne()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Three" }, version: 3);
        for (var number = 1; number <= 3; number++)
        {
            _content.AddVersion(id, number, new JsonObject { ["title"] = $"Title {number}" });
        }

        _content.Diffs[("en", 2, 3)] = new JsonArray(Change("title", "changed", "Title 2", "Title 3"));
        _content.Diffs[("en", 1, 3)] = new JsonArray(Change("title", "changed", "Title 1", "Title 3"));
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.EndsWith("/versions/2/diff/3", StringComparison.Ordinal) ? held.Task : null;
        var app = Load($"content/post/{id:D}");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(3));
        Button(panel, "Compare").Click();
        panel.WaitForAssertion(() => panel.Find(".version-diff p[role='status']"));

        panel.Find(".version-compare select").Change("1");
        Button(panel, "Compare").Click();

        panel.WaitForAssertion(() => panel.Find(".version-diff h3").TextContent.ShouldBe("Changes from version 1 to version 3"));
        held.SetResult();
        DiffRows(panel, "title").ShouldBe([["Title", "Changed", "Title 1", "Title 3"]]);
        Management.Requests.Select(request => request.Path).ShouldContain($"content/post/{id:D}/versions/2/diff/3");
        _content.Reads.ShouldNotContain($"/api/management/v1/content/post/{id:D}/versions/2/diff/3");
    }

    [Fact]
    public void ALocalizedTypeReadsItsVersionsAndItsDiffInTheLocaleOfTheForm()
    {
        Management.AddLocale(MainSite, "fr", 1);
        Management.Schemas[MainSite]["types"]![0]!["localized"] = true;
        var id = _content.Add("post", new JsonObject { ["title"] = "Bonjour" }, version: 2);
        _content.Add("post", new JsonObject { ["title"] = "Salut" }, version: 2, id: id, locale: "fr");
        _content.AddVersion(id, 1, new JsonObject { ["title"] = "Allo" }, locale: "fr");
        _content.AddVersion(id, 2, new JsonObject { ["title"] = "Salut" }, locale: "fr");
        _content.Diffs[("en", 1, 2)] = new JsonArray(Change("title", "changed", "Hi", "Hello"));
        _content.Diffs[("fr", 1, 2)] = new JsonArray(Change("title", "changed", "Allo", "Salut"));
        var app = Load($"content/post/{id:D}?locale=fr");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));
        panel.Find(".version-list caption").TextContent.ShouldBe("Versions in fr, newest first");

        Button(panel, "Compare").Click();

        panel.WaitForAssertion(() => DiffRows(panel, "title").ShouldBe([["Title", "Changed", "Allo", "Salut"]]));
        _content.ReadLocales.Where(read => read.Read.Contains("/versions", StringComparison.Ordinal)).Select(read => read.Locale).Distinct().ShouldBe(["fr"]);
        panel.Find(".version-diff-table td[lang]").GetAttribute("lang").ShouldBe("fr");
    }

    [Fact]
    public void RelationsAreNamedByTheirEntriesAndABlockByTheLabelsOfItsFields()
    {
        var ada = _content.Add("author", new JsonObject { ["name"] = "Ada Lovelace" });
        var hidden = Guid.CreateVersion7();
        var id = TwoVersions();
        _content.Diffs[("en", 1, 2)] = new JsonArray(
            Change("sections[0]", "added", null, new JsonObject { ["type"] = "hero", ["id"] = "h1", ["heading"] = "Welcome" }),
            Change("author", "changed", ada.ToString("D"), hidden.ToString("D")));
        var app = Load($"content/post/{id:D}");
        var panel = OpenHistory(app);
        panel.WaitForAssertion(() => Rows(panel).Count.ShouldBe(2));

        Button(panel, "Compare").Click();

        panel.WaitForAssertion(() => DiffRows(panel, "author").ShouldBe([["Author", "Changed", "Ada Lovelace", $"an entry you may not read ({hidden:D})"]]));
        DiffRows(panel, "sections").ShouldBe([["Hero 1", "Added", "No value", "Heading: Welcome"]]);
        _content.Reads.Count(read => read.StartsWith("/api/management/v1/content/author?", StringComparison.Ordinal)).ShouldBe(1);
    }

    private Guid TwoVersions()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Now" }, version: 2);
        _content.AddVersion(id, 1, new JsonObject { ["title"] = "First" });
        _content.AddVersion(id, 2, new JsonObject { ["title"] = "Now" });
        return id;
    }

    private static JsonObject Change(string path, string kind, JsonNode? before, JsonNode? after) =>
        new() { ["path"] = path, ["kind"] = kind, ["before"] = before, ["after"] = after };

    private static JsonObject Relation(string name, string target)
    {
        var field = Field(name, "relation");
        field["relation"] = new JsonObject { ["target"] = target };
        return field;
    }

    private static JsonObject Blocks(string name, params string[] types) =>
        Field(name, "blocks", settings: new JsonObject { ["blocks"] = new JsonArray([.. types.Select(type => (JsonNode)type)]) });

    private IRenderedComponent<AdminApp> Load(string route)
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    private static IRenderedComponent<CmsDrawer> OpenHistory(IRenderedComponent<AdminApp> app)
    {
        app.FindAll(".content-form-history fluent-button").Single(button => button.TextContent.Trim() == "Version history").Click();
        app.WaitForAssertion(() => Panel(app).Instance.Open.ShouldBeTrue());
        return Panel(app);
    }

    private static IRenderedComponent<CmsDrawer> Panel(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsDrawer>().Single(drawer => drawer.Instance.Title == "Version history");

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    private static List<IElement> Rows(IRenderedComponent<CmsDrawer> panel) => [.. panel.FindAll(".version-list tbody tr")];

    private static string[] Cells(IElement row) => [.. row.Children.Select(cell => cell.TextContent.Trim())];

    private static IElement Button(IRenderedComponent<CmsDrawer> panel, string text) =>
        panel.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static string[][] DiffRows(IRenderedComponent<CmsDrawer> panel, string field) =>
        [.. panel.FindAll($".version-diff-table[data-field='{field}'] tbody tr").Select(Cells)];
}
