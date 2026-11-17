using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The relation field of A-15: its editor and the picker over the target's list, the order of an ordered relation, inline
// create and a related entry in a side panel, the side panel of the list's row action, and the relation filter's picker. An
// article has an author, whose type is searchable, and ordered tags, whose type is not.
public sealed class RelationTests : AdminTestContext
{
    private readonly FakeContent _content;
    private readonly Guid _ada;
    private readonly Guid _grace;

    public RelationTests()
    {
        UseNeutralResources();
        Document.SetupModule(invocation => invocation.Identifier == "rememberFocus");
        Document.SetupVoid("returnFocus", _ => true).SetVoidResult();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Article(), Author(), Tag());
        _ada = _content.Add("author", new JsonObject { ["name"] = "Ada Lovelace" });
        _grace = _content.Add("author", new JsonObject { ["name"] = "Grace Hopper" });
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void AnAuthorIsChosenFromThePickerBySearchAndSavedAsItsId()
    {
        var app = Load("content/article/new");

        Click(FieldOf(app, "author"), "Choose an entry");
        var picker = Picker(app, "Author");
        app.WaitForAssertion(() => Rows(picker).ShouldBe(["Ada Lovelace", "Grace Hopper"]));
        _content.Reads.Last().ShouldContain("fields=id,status,name,updatedAt");
        _content.Reads.Last().ShouldContain("sort=name&pageSize=25");

        SearchIn(picker, "grace");
        app.WaitForAssertion(() => Rows(Picker(app, "Author")).ShouldBe(["Grace Hopper"]));
        _content.Reads.Last().ShouldContain("q=grace");
        Select(Picker(app, "Author"), "Grace Hopper");
        Click(Picker(app, "Author"), "Choose");

        app.WaitForAssertion(() => Labels(app, "author").ShouldBe(["Grace Hopper"]));
        app.FindComponents<CmsDialog>().ShouldNotContain(dialog => dialog.Instance.Title == "Choose an entry: Author" && dialog.Instance.Open);
        Input(app, "title").Input("With an author");
        Save(app);
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Body["author"]!.GetValue<string>().ShouldBe(_grace.ToString("D")));
    }

    [Fact]
    public void TheTagsOfATargetThatIsNotSearchableAreSearchedByAContainsFilterOnTheirLabel()
    {
        _content.Add("tag", new JsonObject { ["name"] = "dotnet" });
        _content.Add("tag", new JsonObject { ["name"] = "headless" });
        var app = Load("content/article/new");

        Click(FieldOf(app, "tags"), "Choose entries");
        app.WaitForAssertion(() => Rows(Picker(app, "Tag")).Count.ShouldBe(2));
        SearchIn(Picker(app, "Tag"), "net");

        app.WaitForAssertion(() => Rows(Picker(app, "Tag")).ShouldBe(["dotnet"]));
        _content.Reads.Last().ShouldContain("filter[name][contains]=net");
        _content.Reads.Last().ShouldNotContain("q=");
    }

    [Fact]
    public void LoadMoreReadsTheNextPageByTheCursorOfTheLast()
    {
        for (var n = 1; n <= 30; n++)
        {
            _content.Add("tag", new JsonObject { ["name"] = $"tag {n:D2}" });
        }

        var app = Load("content/article/new");
        Click(FieldOf(app, "tags"), "Choose entries");
        app.WaitForAssertion(() => Rows(Picker(app, "Tag")).Count.ShouldBe(25));

        Click(Picker(app, "Tag"), "Load more");

        app.WaitForAssertion(() => Rows(Picker(app, "Tag")).Count.ShouldBe(30));
        _content.Reads.Last().ShouldContain("cursor=25");
        ButtonsOf(Picker(app, "Tag")).ShouldNotContain("Load more");
    }

    [Fact]
    public void ARelationToManyTakesSeveralEntriesAndKeepsThoseChosenBeforeInTheirOrder()
    {
        var first = _content.Add("tag", new JsonObject { ["name"] = "first" });
        var second = _content.Add("tag", new JsonObject { ["name"] = "second" });
        var third = _content.Add("tag", new JsonObject { ["name"] = "third" });
        var id = _content.Add("article", new JsonObject { ["title"] = "Tagged", ["tags"] = Ids(third) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["third"]));

        Click(FieldOf(app, "tags"), "Choose entries");
        app.WaitForAssertion(() => Rows(Picker(app, "Tag")).Count.ShouldBe(3));
        Checked(Picker(app, "Tag")).ShouldBe(["third"]);
        Select(Picker(app, "Tag"), "first");
        Select(Picker(app, "Tag"), "second");
        Click(Picker(app, "Tag"), "Choose");

        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["third", "first", "second"]));
        Save(app);
        app.WaitForAssertion(() => Strings(_content.Writes.ShouldHaveSingleItem().Body["tags"]).ShouldBe([Text(third), Text(first), Text(second)]));
    }

    [Fact]
    public void AnOrderedRelationKeepsTheOrderGivenByButtonsByKeysAndByDrag()
    {
        var a = _content.Add("tag", new JsonObject { ["name"] = "a" });
        var b = _content.Add("tag", new JsonObject { ["name"] = "b" });
        var c = _content.Add("tag", new JsonObject { ["name"] = "c" });
        var id = _content.Add("article", new JsonObject { ["title"] = "Ordered", ["tags"] = Ids(a, b, c) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b", "c"]));

        IconButton(FieldOf(app, "tags"), "Move a down").Click();
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "a", "c"]));
        Announced(app).ShouldContain("a moved to position 2 of 3.");

        Items(app, "tags")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "c", "a"]));

        Items(app, "tags")[2].DragStart();
        Items(app, "tags")[0].DragOver();
        Items(app, "tags")[0].Drop();
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b", "c"]));

        IconButton(FieldOf(app, "tags"), "Move a up").HasAttribute("disabled").ShouldBeTrue();
        FieldOf(app, "tags").QuerySelector("fieldset")!.GetAttribute("aria-describedby").ShouldNotBeNull();
        Save(app);
        app.WaitForAssertion(() => Strings(_content.Writes.ShouldHaveSingleItem().Body["tags"]).ShouldBe([Text(a), Text(b), Text(c)]));
    }

    // Focus follows the moved entry to its new place, at an end and in the middle, by keys to its entry and by a button to
    // that button, or to the other one when the pressed one became disabled at an end.
    [Fact]
    public void FocusFollowsTheMovedEntry()
    {
        var a = _content.Add("tag", new JsonObject { ["name"] = "a" });
        var b = _content.Add("tag", new JsonObject { ["name"] = "b" });
        var c = _content.Add("tag", new JsonObject { ["name"] = "c" });
        var id = _content.Add("article", new JsonObject { ["title"] = "Ordered", ["tags"] = Ids(a, b, c) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b", "c"]));

        Items(app, "tags")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "a", "c"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "openButtons", 1)));

        Items(app, "tags")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "c", "a"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "openButtons", 2)));

        Items(app, "tags")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        Items(app, "tags")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b", "c"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "openButtons", 0)));

        IconButton(FieldOf(app, "tags"), "Move a down").Click();
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "a", "c"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "downButtons", 1)));

        IconButton(FieldOf(app, "tags"), "Move a down").Click();
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "c", "a"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "upButtons", 2)));

        IconButton(FieldOf(app, "tags"), "Move b up").HasAttribute("disabled").ShouldBeTrue();
        IconButton(FieldOf(app, "tags"), "Move c up").Click();
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["c", "b", "a"]));
        app.WaitForAssertion(() => Focused().ShouldBe(ButtonAt(app, "downButtons", 0)));
    }

    // A move changes what the items show rather than moving them in the document, so the element that had focus stays.
    [Fact]
    public void AMoveKeepsTheItemsInPlace()
    {
        var a = _content.Add("tag", new JsonObject { ["name"] = "a" });
        var b = _content.Add("tag", new JsonObject { ["name"] = "b" });
        var id = _content.Add("article", new JsonObject { ["title"] = "Ordered", ["tags"] = Ids(a, b) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b"]));
        var before = ButtonAt(app, "openButtons", 1);

        Items(app, "tags")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });

        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b", "a"]));
        ButtonAt(app, "openButtons", 1).ShouldBe(before);
        Focused().ShouldBe(ButtonAt(app, "openButtons", 0));
    }

    [Fact]
    public void AnEntryTheValueHoldsTwiceKeepsBothPlacesWhenMovedAndWhenChosenAgain()
    {
        var a = _content.Add("tag", new JsonObject { ["name"] = "a" });
        var b = _content.Add("tag", new JsonObject { ["name"] = "b" });
        var id = _content.Add("article", new JsonObject { ["title"] = "Twice", ["tags"] = Ids(a, b, a) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b", "a"]));

        Items(app, "tags")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "a", "b"]));
        Items(app, "tags")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "a", "b"]));

        Click(FieldOf(app, "tags"), "Choose entries");
        app.WaitForAssertion(() => Rows(Picker(app, "Tag")).Count.ShouldBe(2));
        Click(Picker(app, "Tag"), "Choose");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "a", "b"]));
        Save(app);
        app.WaitForAssertion(() => Strings(_content.Writes.ShouldHaveSingleItem().Body["tags"]).ShouldBe([Text(a), Text(a), Text(b)]));
    }

    [Fact]
    public void AnEntryThatCannotBeReadShowsItsIdAndAFailedReadIsTriedAgain()
    {
        var gone = Guid.CreateVersion7();
        var failures = 1;
        Management.Answer = (method, path) => path == "content/author" && failures-- > 0 ? new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) : null;
        var id = _content.Add("article", new JsonObject { ["title"] = "Two authors", ["author"] = Text(_ada), ["tags"] = Ids(gone) });
        var app = Load($"content/article/{id:D}");

        app.WaitForAssertion(() => Labels(app, "author").ShouldBe([Text(_ada)]));
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe([Text(gone)]));
        Input(app, "title").Input("Typed");

        app.WaitForAssertion(() => Labels(app, "author").ShouldBe(["Ada Lovelace"]));
        Labels(app, "tags").ShouldBe([Text(gone)]);
    }

    // The shell reads in the site's default locale until A-17, so the form is given its locale as a panel's opener gives it.
    [Fact]
    public async Task ThePickerAndThePanelReadInTheLocaleOfTheForm()
    {
        var author = Author();
        author["localized"] = true;
        Management.SetTypes(MainSite, Article(), author, Tag());
        _content.Add("author", new JsonObject { ["name"] = "Ada Lovelace" }, id: _ada, locale: "fr");
        _content.Add("author", new JsonObject { ["name"] = "Grace Hopper" }, id: _grace, locale: "fr");
        var id = _content.Add("article", new JsonObject { ["title"] = "En français", ["author"] = Text(_ada) }, locale: "fr");
        Start();
        await Services.GetRequiredService<AdminContext>().StartAsync();
        var form = Render<FluentCMS.Admin.Content.ContentForm>(parameters => parameters.Add(x => x.Type, "article").Add(x => x.Id, id).Add(x => x.Locale, "fr"));

        form.WaitForAssertion(() => form.Find("[data-field='author'] .relation-field-name").TextContent.ShouldBe("Ada Lovelace"));
        _content.Reads.ShouldContain(read => read.StartsWith("/api/management/v1/content/author?filter[id][in]=", StringComparison.Ordinal) && read.EndsWith("&locale=fr", StringComparison.Ordinal));

        Click(form.Find("[data-field='author'] fieldset"), "Choose an entry");
        form.WaitForAssertion(() => _content.Reads.Last().ShouldStartWith("/api/management/v1/content/author?sort=name&"));
        _content.Reads.Last().ShouldContain("&locale=fr&");
        Click(form.Find("[data-field='tags'] fieldset"), "Choose entries");
        form.WaitForAssertion(() => _content.Reads.Last().ShouldStartWith("/api/management/v1/content/tag?"));
        _content.Reads.Last().ShouldNotContain("locale=");

        form.Find("[data-field='author'] .relation-field-entry fluent-button").Click();
        form.WaitForAssertion(() => form.FindComponents<CmsDrawer>().Single(drawer => drawer.Instance.Title == "Author").Find("h2").TextContent.ShouldBe("Ada Lovelace"));
        _content.ReadLocales.Last(read => read.Read.StartsWith($"/api/management/v1/content/author/{_ada:D}", StringComparison.Ordinal)).Locale.ShouldBe("fr");
    }

    [Fact]
    public async Task APanelKeepsItsDraftApartFromThePagesForms()
    {
        var app = Load("content/article/new");
        Click(FieldOf(app, "author"), "New Author");
        app.WaitForAssertion(() => Panel(app, "Author").Find("[data-field='name'] input"));
        Panel(app, "Author").Find("[data-field='name'] input").Input("Unsaved in the panel");

        await Services.GetRequiredService<FluentCMS.Admin.Forms.FormDrafts>().SaveTrackedAsync("user");

        var stored = System.Text.Json.JsonSerializer.Deserialize(Storage.Items[FluentCMS.Admin.Forms.FormDrafts.StorageKey], FluentCMS.Admin.Authentication.AuthenticationJsonContext.Default.ListStoredDraft)!;
        stored.ShouldHaveSingleItem().Type.ShouldBe("panel:author");
        stored[0].Fields["name"]!.GetValue<string>().ShouldBe("Unsaved in the panel");
    }

    [Fact]
    public void ARelationThatIsNotOrderedHasNoOrderControlsAndAnEntryIsRemoved()
    {
        Management.SetTypes(MainSite, Article(orderedTags: false), Author(), Tag());
        var a = _content.Add("tag", new JsonObject { ["name"] = "a" });
        var b = _content.Add("tag", new JsonObject { ["name"] = "b" });
        var id = _content.Add("article", new JsonObject { ["title"] = "Unordered", ["tags"] = Ids(a, b) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["a", "b"]));

        Items(app, "tags").ShouldAllBe(item => !item.HasAttribute("draggable"));
        FieldOf(app, "tags").QuerySelectorAll("fluent-button[aria-label^='Move']").ShouldBeEmpty();
        IconButton(FieldOf(app, "tags"), "Remove a").Click();

        app.WaitForAssertion(() => Labels(app, "tags").ShouldBe(["b"]));
        Announced(app).ShouldContain("a removed.");
    }

    [Fact]
    public void AnAuthorCreatedInTheSidePanelIsChosenWhenSavedAndTheArticleKeepsItsChanges()
    {
        var app = Load("content/article/new");
        Input(app, "title").Input("Not saved yet");

        Click(FieldOf(app, "author"), "New Author");
        var panel = Panel(app, "Author");
        app.WaitForAssertion(() => panel.Find("h2").TextContent.ShouldBe("Author: new entry"));
        panel.Instance.Open.ShouldBeTrue();
        Document.VerifyInvoke("rememberFocus");
        panel.Find("[data-field='name'] input").Input("Hedy Lamarr");
        Click(Panel(app, "Author"), "Save");

        app.WaitForAssertion(() => Labels(app, "author").ShouldBe(["Hedy Lamarr"]));
        var create = _content.Writes.ShouldHaveSingleItem();
        (create.Method, create.Path).ShouldBe((HttpMethod.Post, "content/author"));
        Navigation.Uri.ShouldEndWith("content/article/new");
        Panel(app, "Author").Instance.Open.ShouldBeTrue();
        Announced(app).ShouldContain("Hedy Lamarr created and chosen.");

        ClosePanel(app, "Author");

        app.WaitForAssertion(() => Panel(app, "Author").Instance.Open.ShouldBeFalse());
        Input(app, "title").GetAttribute("value").ShouldBe("Not saved yet");
        app.WaitForAssertion(() => Document.VerifyInvoke("returnFocus"));
        Save(app);
        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        _content.Writes[1].Body["title"]!.GetValue<string>().ShouldBe("Not saved yet");
        var author = Guid.Parse(_content.Writes[1].Body["author"]!.GetValue<string>());
        _content.Fields(author)["name"]!.GetValue<string>().ShouldBe("Hedy Lamarr");
    }

    [Fact]
    public void InlineCreateIsAbsentForACallerWhoMayNotCreateEntriesOfTheTarget()
    {
        Management.Access[MainSite]["types"]!["author"]!["create"] = false;
        var app = Load("content/article/new");

        ButtonsOf(FieldOf(app, "author").QuerySelector("fieldset")!).ShouldBe(["Choose an entry"]);
        ButtonsOf(FieldOf(app, "tags").QuerySelector("fieldset")!).ShouldBe(["Choose entries", "New Tag"]);
    }

    [Fact]
    public void ARelatedEntryOpensInASidePanelAndClosesBackToTheControlThatOpenedIt()
    {
        // A read gives a relation as a reference.
        var id = _content.Add("article", new JsonObject { ["title"] = "By Ada", ["author"] = new JsonObject { ["id"] = Text(_ada), ["type"] = "author" } });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "author").ShouldBe(["Ada Lovelace"]));

        FieldOf(app, "author").QuerySelector(".relation-field-entry fluent-button")!.Click();

        var panel = Panel(app, "Author");
        app.WaitForAssertion(() => panel.Find("h2").TextContent.ShouldBe("Ada Lovelace"));
        panel.Instance.Open.ShouldBeTrue();
        Document.VerifyInvoke("rememberFocus");
        Document.VerifyNotInvoke("returnFocus");
        app.Find("main h1").TextContent.ShouldBe("By Ada");

        ClosePanel(app, "Author");

        app.WaitForAssertion(() => Document.VerifyInvoke("returnFocus"));
        Panel(app, "Author").Instance.Open.ShouldBeFalse();
        Panel(app, "Author").FindAll(".content-form").ShouldBeEmpty();
    }

    [Fact]
    public async Task TheSidePanelAsksBeforeItClosesWithUnsavedChanges()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "By Grace", ["author"] = Text(_grace) });
        var app = Load($"content/article/{id:D}");
        app.WaitForAssertion(() => Labels(app, "author").ShouldBe(["Grace Hopper"]));
        FieldOf(app, "author").QuerySelector(".relation-field-entry fluent-button")!.Click();
        app.WaitForAssertion(() => Panel(app, "Author").Find("[data-field='name'] input"));
        Panel(app, "Author").Find("[data-field='name'] input").Input("Grace Brewster Hopper");

        ClosePanel(app, "Author");

        app.WaitForAssertion(() => Asking(app).ShouldBeTrue());
        Panel(app, "Author").Instance.Open.ShouldBeTrue();
        Click(Dialog(app, "Close with unsaved changes?"), "Keep editing");
        app.WaitForAssertion(() => Asking(app).ShouldBeFalse());
        Panel(app, "Author").Find("[data-field='name'] input").GetAttribute("value").ShouldBe("Grace Brewster Hopper");

        // Escape closes the drawer itself; with the changes kept it opens again.
        var drawer = Panel(app, "Author");
        await drawer.InvokeAsync(() => drawer.Instance.OpenChanged.InvokeAsync(false));
        app.WaitForAssertion(() => Asking(app).ShouldBeTrue());
        Panel(app, "Author").Instance.Open.ShouldBeFalse();
        Click(Dialog(app, "Close with unsaved changes?"), "Keep editing");
        app.WaitForAssertion(() => Panel(app, "Author").Instance.Open.ShouldBeTrue());

        ClosePanel(app, "Author");
        app.WaitForAssertion(() => Asking(app).ShouldBeTrue());
        Click(Dialog(app, "Close with unsaved changes?"), "Close without saving");

        app.WaitForAssertion(() => Panel(app, "Author").Instance.Open.ShouldBeFalse());
        _content.Writes.ShouldBeEmpty();
        Labels(app, "author").ShouldBe(["Grace Hopper"]);
        Services.GetRequiredService<FluentCMS.Admin.Forms.FormDrafts>().HasTracked.ShouldBeFalse();
    }

    [Fact]
    public void TheListOpensAnEntryInASidePanelAndReadsAgainOnceItWasSaved()
    {
        var app = Load("content/author", form: false);
        var reads = _content.Reads.Count(read => read.StartsWith("/api/management/v1/content/author?", StringComparison.Ordinal));

        // Enter on the row's button is the button's, not the row's, which would open the entry's page.
        IconButton(app.Find(".cms-data-grid"), "Open Ada Lovelace in a side panel").ParentElement!.OuterHtml.ShouldContain("onkeydown:stoppropagation", Case.Insensitive);
        IconButton(app.Find(".cms-data-grid"), "Open Ada Lovelace in a side panel").Click();
        var panel = Panel(app, "Author");
        app.WaitForAssertion(() => panel.Find("h2").TextContent.ShouldBe("Ada Lovelace"));
        Navigation.Uri.ShouldEndWith("content/author");
        panel.Find("[data-field='name'] input").Input("Augusta Ada King");
        Click(Panel(app, "Author"), "Save");
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put));
        ClosePanel(app, "Author");

        app.WaitForAssertion(() => _content.Reads.Count(read => read.StartsWith("/api/management/v1/content/author?", StringComparison.Ordinal)).ShouldBe(reads + 1));
        app.WaitForAssertion(() => app.Find(".cms-data-grid tbody").TextContent.ShouldContain("Augusta Ada King"));
    }

    [Fact]
    public void TheRelationFilterOfTheListTakesThePicker()
    {
        var app = Load("content/article", form: false);

        Click(app.Find(".content-list-toolbar"), "Add filter");
        Choose(app, "Field", "author");
        Click(Dialog(app, "Add a filter"), "Choose an entry");
        app.WaitForAssertion(() => Rows(Picker(app, "Author")).ShouldBe(["Ada Lovelace", "Grace Hopper"]));
        Select(Picker(app, "Author"), "Ada Lovelace");
        Click(Picker(app, "Author"), "Choose");
        app.WaitForAssertion(() => Dialog(app, "Add a filter").Find(".content-filter-related").TextContent.ShouldBe("Ada Lovelace"));
        Click(Dialog(app, "Add a filter"), "Apply filter");

        app.WaitForAssertion(() => Navigation.Uri.ShouldEndWith($"content/article?filter[author][eq]={_ada:D}"));
        app.WaitForAssertion(() => app.Find(".content-filter fluent-button").TextContent.Trim().ShouldBe("Author is Ada Lovelace"));
    }

    [Fact]
    public void TheCellShowsTheLabelsOfTheRelatedEntries()
    {
        var app = Load("content/article?columns=author,tags", form: false, entries: true);

        app.WaitForAssertion(() => Cells(app).ShouldBe(["Ada Lovelace", "b, a"]));
    }

    private IRenderedComponent<AdminApp> Load(string route, bool form = true, bool entries = false)
    {
        if (entries)
        {
            Management.Entries["article"] =
            [
                new JsonObject
                {
                    ["id"] = Guid.CreateVersion7(),
                    ["status"] = "draft",
                    ["title"] = "Listed",
                    ["author"] = new JsonObject { ["id"] = Text(_ada), ["name"] = "Ada Lovelace" },
                    ["tags"] = new JsonArray(new JsonObject { ["id"] = Text(Guid.CreateVersion7()), ["name"] = "b" }, new JsonObject { ["id"] = Text(Guid.CreateVersion7()), ["name"] = "a" }),
                },
            ];
            Management.Respond = null;
        }

        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        if (form)
        {
            app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
            app.WaitForAssertion(() => app.Find("[data-field='author'] fieldset"));
        }
        else
        {
            app.WaitForAssertion(() => app.FindAll("table[aria-busy]").ShouldBeEmpty());
        }

        return app;
    }

    // The page's form comes first in the document, before a side panel's in one of its fields.
    private static void Save(IRenderedComponent<AdminApp> app) =>
        app.FindAll(".content-form-toolbar fluent-button").First(button => button.TextContent.Trim() == "Save").Click();

    private static IElement FieldOf(IRenderedComponent<AdminApp> app, string name) => app.Find($"main [data-field='{name}']");

    private static IElement Input(IRenderedComponent<AdminApp> app, string name) => app.Find($"[data-field='{name}'] input");

    private static List<IElement> Items(IRenderedComponent<AdminApp> app, string name) => [.. FieldOf(app, name).QuerySelectorAll(".relation-field-item")];

    private static List<string> Labels(IRenderedComponent<AdminApp> app, string name) =>
        [.. FieldOf(app, name).QuerySelectorAll(".relation-field-entry").Select(entry => entry.QuerySelector(".relation-field-name")?.TextContent ?? entry.TextContent.Trim())];

    private static List<string> ButtonsOf(IElement container) =>
        [.. container.QuerySelectorAll("fluent-button").Select(button => button.TextContent.Trim()).Where(text => text.Length > 0)];

    private static List<string> ButtonsOf(IRenderedComponent<CmsDialog> dialog) =>
        [.. dialog.FindAll("fluent-button").Select(button => button.TextContent.Trim()).Where(text => text.Length > 0)];

    private static void Click(IElement container, string text) =>
        container.QuerySelectorAll("fluent-button").First(button => button.TextContent.Trim() == text).Click();

    private static void Click<T>(IRenderedComponent<T> component, string text)
        where T : IComponent =>
        component.FindAll("fluent-button").First(button => button.TextContent.Trim() == text).Click();

    // The picker's own button, after the Cancel of its filter dialog.
    private static void CancelPicker(IRenderedComponent<AdminApp> app, string type) =>
        Picker(app, type).FindAll("fluent-button").Last(button => button.TextContent.Trim() == "Cancel").Click();

    // The panel's own button, after those of the dialogs of its form.
    private static void ClosePanel(IRenderedComponent<AdminApp> app, string title) =>
        Panel(app, title).FindAll("fluent-button").Last(button => button.TextContent.Trim() == "Close").Click();

    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => ((ElementReference)call.Arguments[0]!).Id).LastOrDefault();

    // The element of a button the tags editor holds by position, read from the editor, since bUnit's markup does not name an
    // element once it has rendered again.
    private static string ButtonAt(IRenderedComponent<AdminApp> app, string buttons, int position)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var editor = app.FindComponents<FluentCMS.Admin.Fields.RelationFieldEditor>().Single(found => found.Instance.Field.Name == "tags").Instance;
        var button = ((Dictionary<int, CmsButton>)editor.GetType().GetField(buttons, Flags)!.GetValue(editor)!)[position];
        var library = typeof(CmsButton).GetField("_button", Flags)!.GetValue(button)!;
        return ((ElementReference)library.GetType().GetProperty("Element")!.GetValue(library)!).Id;
    }

    private static IElement IconButton(IElement container, string label) => container.QuerySelector($"fluent-button[aria-label='{label}']")!;

    // A relation to one chooses an entry, and one to many entries.
    private static IRenderedComponent<CmsDialog> Picker(IRenderedComponent<AdminApp> app, string type) =>
        app.FindComponents<CmsDialog>().Single(dialog =>
            (dialog.Instance.Title == $"Choose an entry: {type}" && type == "Author" || dialog.Instance.Title == $"Choose entries: {type}" && type == "Tag")
            && dialog.Instance.Open);

    private static IRenderedComponent<CmsDrawer> Panel(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDrawer>().Single(drawer => drawer.Instance.Title == title);

    // Each relation field has its own panel and question; the open one, or the one there is.
    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title)
    {
        var dialogs = app.FindComponents<CmsDialog>().Where(dialog => dialog.Instance.Title == title).ToList();
        return dialogs.Count == 1 ? dialogs[0] : dialogs.Single(dialog => dialog.Instance.Open);
    }

    private static bool Asking(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsDialog>().Any(dialog => dialog.Instance.Title == "Close with unsaved changes?" && dialog.Instance.Open);

    private static List<string> Rows(IRenderedComponent<CmsDialog> picker) =>
        [.. picker.FindAll(".cms-data-grid tbody tr:not(.cms-data-grid-message)").Select(row => row.QuerySelectorAll("td")[1].TextContent.Trim())];

    private static List<string> Checked(IRenderedComponent<CmsDialog> picker) =>
        [.. picker.FindAll(".cms-data-grid tbody tr[aria-selected=true]").Select(row => row.QuerySelectorAll("td")[1].TextContent.Trim())];

    private static void Select(IRenderedComponent<CmsDialog> picker, string label) =>
        picker.FindAll(".cms-data-grid tbody tr").Single(row => row.QuerySelectorAll("td")[1].TextContent.Trim() == label)
            .QuerySelector("input[type=checkbox]")!.Change(true);

    private static void SearchIn(IRenderedComponent<CmsDialog> picker, string text)
    {
        var input = picker.FindComponents<CmsTextInput>().Single(found => found.Instance.Label == "Search");
        input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(text)).GetAwaiter().GetResult();
        picker.Find("form[role=search]").Submit();
    }

    private static void Choose(IRenderedComponent<AdminApp> app, string label, string value)
    {
        var input = app.FindComponents<CmsSelect>().Single(found => found.Instance.Label == label);
        input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value)).GetAwaiter().GetResult();
    }

    private static List<string> Cells(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".cms-data-grid tbody tr")[0].QuerySelectorAll("td:not(.cms-data-grid-select)").Skip(1).SkipLast(1).Select(cell => cell.TextContent.Trim())];

    private static List<string> Announced(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[role=status] > div").Select(message => message.TextContent)];

    private static string Text(Guid id) => id.ToString("D");

    private static JsonArray Ids(params Guid[] ids) => new([.. ids.Select(id => (JsonNode)Text(id))]);

    private static List<string> Strings(JsonNode? node) => [.. node!.AsArray().Select(item => item!.GetValue<string>())];

    private static JsonObject Article(bool orderedTags = true)
    {
        var author = Field("author", "relation");
        author["relation"] = new JsonObject { ["target"] = "author" };
        var tags = Field("tags", "relation");
        tags["multiple"] = true;
        tags["relation"] = new JsonObject { ["target"] = "tag", ["ordered"] = orderedTags };
        return Type("article", fields: [Field("title", required: true), author, tags]);
    }

    private static JsonObject Author()
    {
        var name = Field("name", required: true);
        name["searchable"] = true;
        return Type("author", fields: [name]);
    }

    private static JsonObject Tag() => Type("tag", fields: [Field("name", required: true)]);
}
