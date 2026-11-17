using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The behaviours of the form (A-49): conditions on every change, the rules checked as a field is left and required before a
// publish, the fields the caller may change, the question before unsaved changes are left, the shortcuts and the dialog of
// a conflicting save.
public sealed class ContentFormBehaviourTests : AdminTestContext
{
    private const string SignedInUser = "0193a1b2-0000-7000-8000-000000000001";
    private static readonly Guid Shop = new("0193a1b2-0000-7000-8000-00000000a002");

    private readonly FakeContent _content;

    public ContentFormBehaviourTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Link());
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void AFieldWithVisibleWhenAppearsAndHidesAsItsSourceChangesAndKeepsItsValue()
    {
        var app = Load("content/link/new");
        app.FindAll("[data-field='url']").ShouldBeEmpty();

        Input(app, "kind").Input("external");

        app.WaitForAssertion(() => Input(app, "url"));
        Input(app, "url").Input("https://example.com");
        Input(app, "kind").Input("internal");

        app.WaitForAssertion(() => app.FindAll("[data-field='url']").ShouldBeEmpty());
        Save(app);
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Body["url"]!.GetValue<string>().ShouldBe("https://example.com"));
    }

    [Fact]
    public void AFieldWithRequiredWhenTurnsRequiredAsItsSourceChanges()
    {
        var app = Load("content/link/new");
        Input(app, "summary").HasAttribute("aria-required").ShouldBeFalse();

        Input(app, "kind").Input("external");

        app.WaitForAssertion(() => Input(app, "summary").GetAttribute("aria-required").ShouldBe("true"));
        Input(app, "kind").Input("");
        app.WaitForAssertion(() => Input(app, "summary").HasAttribute("aria-required").ShouldBeFalse());
    }

    [Fact]
    public void ALengthPatternOrRangeMistakeShowsOnLeavingTheFieldWithoutARequest()
    {
        var app = Load("content/link/new");
        var requests = Management.Calls.Count;

        Input(app, "title").Input("A title that is far too long");
        Input(app, "title").HasAttribute("aria-invalid").ShouldBeFalse();
        Input(app, "title").Blur();
        Input(app, "slug").Input("Not A Slug");
        Input(app, "slug").Blur();
        Input(app, "rating").Change("9");
        Input(app, "rating").Blur();

        app.WaitForAssertion(() => Message(app, "title").ShouldBe("Enter at most 10 characters. This has 28."));
        Input(app, "title").GetAttribute("aria-invalid").ShouldBe("true");
        Message(app, "slug").ShouldBe("The value does not have the expected format.");
        Message(app, "rating").ShouldBe("Enter 5 or less.");
        Management.Calls.Count.ShouldBe(requests);

        // Fixed, the finding goes as it is typed.
        Input(app, "title").Input("Short");
        app.WaitForAssertion(() => Input(app, "title").HasAttribute("aria-invalid").ShouldBeFalse());
    }

    [Fact]
    public void ASaveWithAMistakeSendsNothingAndFocusesTheField()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");

        Input(app, "slug").Input("Not A Slug");
        Save(app);

        app.WaitForAssertion(() => app.Find(".content-form-problem").TextContent.ShouldContain("The entry was not saved. Correct the problems shown"));
        _content.Writes.ShouldBeEmpty();
        Message(app, "slug").ShouldBe("The value does not have the expected format.");
        app.WaitForAssertion(() => Focused().ShouldBe(InputReference(app, "Slug")));
    }

    [Fact]
    public async Task RequiredIsMarkedWhileEditingAndCheckedBeforeAPublishOnly()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        var form = app.FindComponent<ContentForm>();
        var published = 0;
        form.Instance.Publish = () =>
        {
            published++;
            return Task.CompletedTask;
        };
        Input(app, "title").GetAttribute("aria-required").ShouldBe("true");
        Input(app, "title").Input("");

        // A draft skips required.
        Save(app);
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());

        await Shortcut(FormShortcut.Publish);

        app.WaitForAssertion(() => app.Find(".content-form-problem").TextContent.ShouldContain("The entry was not published."));
        published.ShouldBe(0);
        Message(app, "title").ShouldBe("A value is required to publish.");

        Input(app, "title").Input("Back");
        app.WaitForAssertion(() => Input(app, "title").HasAttribute("aria-invalid").ShouldBeFalse());
        await Shortcut(FormShortcut.Publish);
        app.WaitForAssertion(() => published.ShouldBe(1));
    }

    [Fact]
    public void AFieldTheCallerMayNotChangeIsReadOnlyAndSavedUnchanged()
    {
        Writable(update: ["title", "kind"]);
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello", ["summary"] = "Kept as it is" });
        var app = Load($"content/link/{id:D}");

        Input(app, "summary").HasAttribute("readonly").ShouldBeTrue();
        Input(app, "title").HasAttribute("readonly").ShouldBeFalse();
        Input(app, "title").Input("Changed");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        _content.Writes[0].Body["summary"]!.GetValue<string>().ShouldBe("Kept as it is");
        _content.Writes[0].Body["title"]!.GetValue<string>().ShouldBe("Changed");
    }

    [Fact]
    public void ACreateLeavesOutTheDefaultOfAFieldTheCallerMayNotSet()
    {
        Writable(create: ["title", "kind"]);
        var app = Load("content/link/new");

        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        _content.Writes[0].Body.ContainsKey("rating").ShouldBeFalse();
        _content.Writes[0].Body["title"]!.GetValue<string>().ShouldBe("New link");
    }

    [Fact]
    public void SaveIsAbsentForACallerWhoMayNotUpdateTheType()
    {
        Writable(update: [], mayUpdate: false);
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");

        app.FindAll("fluent-button").Where(button => button.TextContent.Trim() == "Save").ShouldBeEmpty();
        app.Find(".content-form-notice").TextContent.ShouldBe("You can read this entry but not change it.");
        Input(app, "title").HasAttribute("readonly").ShouldBeTrue();
    }

    [Fact]
    public void ARouteChangeWithUnsavedChangesAsksFirstAndNotOtherwise()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        var entry = Navigation.Uri;

        Input(app, "title").Input("Mine");
        Navigation.NavigateTo("not-found");

        app.WaitForAssertion(() => Dialog(app, "Leave with unsaved changes?").Instance.Open.ShouldBeTrue());
        Navigation.Uri.ShouldBe(entry);
        Button(app, "Keep editing").Click();
        Dialog(app, "Leave with unsaved changes?").Instance.Open.ShouldBeFalse();
        Navigation.Uri.ShouldBe(entry);
        Input(app, "title").GetAttribute("value").ShouldBe("Mine");

        Navigation.NavigateTo("not-found");
        app.WaitForAssertion(() => Button(app, "Leave").Click());

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/not-found"));
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void ARouteChangeWithoutUnsavedChangesDoesNotAsk()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        Input(app, "title").Input("Mine");
        Save(app);
        app.WaitForAssertion(() => app.Find(".content-form-saved"));

        Navigation.NavigateTo("not-found");

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/not-found"));
    }

    [Fact]
    public void ASiteSwitchWithUnsavedChangesAsksFirst()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(Shop, Link());
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        Input(app, "title").Input("Mine");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        var dialog = Dialog(app, "Switch site with unsaved changes?");
        dialog.Instance.Open.ShouldBeTrue();
        Services.GetRequiredService<AdminContext>().Site!.Id.ShouldBe(MainSite);
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Keep working").Click();
        app.Find(".site-switcher select").GetAttribute("value").ShouldBe(MainSite.ToString("D"));
        Input(app, "title").GetAttribute("value").ShouldBe("Mine");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));
        Dialog(app, "Switch site with unsaved changes?").FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Switch site").Click();

        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Site!.Id.ShouldBe(Shop));
        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/content/link"));
        app.FindComponents<CmsDialog>().ShouldNotContain(found => found.Instance.Title == "Leave with unsaved changes?" && found.Instance.Open);
    }

    [Fact]
    public void ASiteSwitchWithoutUnsavedChangesDoesNotAsk()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(Shop, Link());
        var app = Load("content/link/new");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));

        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Site!.Id.ShouldBe(Shop));
    }

    [Fact]
    public async Task AClosedTabAsksWhileTheFormIsUnsavedUntilItIsSavedOrGivenUp()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        UnloadGuards().ShouldBeEmpty();

        Input(app, "title").Input("Mine");
        app.WaitForAssertion(() => UnloadGuards().ShouldBe([true]));
        Input(app, "title").Input("Mine again");
        Save(app);

        app.WaitForAssertion(() => UnloadGuards().ShouldBe([true, false]));
        Input(app, "title").Input("Once more");
        app.WaitForAssertion(() => UnloadGuards().ShouldBe([true, false, true]));

        // What the session does at a sign-out the user confirmed.
        await app.InvokeAsync(async () => await Services.GetRequiredService<FormDrafts>().ClearAsync());

        UnloadGuards().ShouldBe([true, false, true, false]);
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public async Task CtrlSSavesAndQuestionMarkListsTheShortcuts()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        Input(app, "title").Input("Mine");

        await Shortcut(FormShortcut.Save);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Body["title"]!.GetValue<string>().ShouldBe("Mine"));

        await Shortcut(FormShortcut.Help);

        var list = Dialog(app, "Keyboard shortcuts");
        app.WaitForAssertion(() => list.Instance.Open.ShouldBeTrue());
        list.FindAll("dd").Select(item => item.TextContent).ShouldBe(["Save the entry", "Close a dialog or a side panel", "Show this list"]);

        // While a dialog is open the form's shortcuts wait.
        await Shortcut(FormShortcut.Save);
        _content.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ThePublishShortcutIsListedAndRunsOnceAPublishIsGiven()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        await Shortcut(FormShortcut.Publish);
        var published = 0;
        app.FindComponent<ContentForm>().Instance.Publish = () =>
        {
            published++;
            return Task.CompletedTask;
        };

        await Shortcut(FormShortcut.Publish);
        await Shortcut(FormShortcut.Help);

        app.WaitForAssertion(() => published.ShouldBe(1));
        app.WaitForAssertion(() => Dialog(app, "Keyboard shortcuts").FindAll("dd").Select(item => item.TextContent).ShouldContain("Publish the entry"));
        Dialog(app, "Keyboard shortcuts").FindAll("dt").Select(item => item.TextContent).ShouldContain("Ctrl+Shift+Enter, or ⌘+Shift+Enter on a Mac");
    }

    [Fact]
    public void A412OpensTheDialogWithTheUsersChangesBesideTheSavedVersion()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello", ["summary"] = "Before" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "summary", "Theirs");
        Input(app, "title").Input("Mine");

        Save(app);

        var dialog = Dialog(app, "Someone saved this entry first");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        Rows(dialog).ShouldBe([("Title", "Mine", "Hello"), ("Summary", "Before", "Theirs")]);
    }

    [Fact]
    public void KeepMineSavesTheUsersChangesOnTheSavedVersion()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello", ["summary"] = "Before" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "summary", "Theirs");
        Input(app, "title").Input("Mine");
        Save(app);
        app.WaitForAssertion(() => Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeTrue());

        Button(app, "Keep mine").Click();

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        _content.Writes[1].IfMatch.ShouldBe("\"2\"");
        app.WaitForAssertion(() => _content.Version(id).ShouldBe(3));
        (_content.Fields(id)["title"]!.GetValue<string>(), _content.Fields(id)["summary"]!.GetValue<string>()).ShouldBe(("Mine", "Theirs"));
        app.WaitForAssertion(() => app.Find(".content-form-saved"));
        Input(app, "summary").GetAttribute("value").ShouldBe("Theirs");
        Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeFalse();
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void TakeTheirsDropsTheUsersChangesAndShowsTheSavedVersion()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "title", "Theirs");
        Input(app, "title").Input("Mine");
        Save(app);
        app.WaitForAssertion(() => Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeTrue());

        Button(app, "Take theirs").Click();

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Theirs"));
        _content.Writes.Count.ShouldBe(1);
        app.FindAll(".content-form-problem").ShouldBeEmpty();
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();

        // The form is on the saved version now, so its next save is no conflict.
        Input(app, "title").Input("Mine again");
        Save(app);
        app.WaitForAssertion(() => _content.Version(id).ShouldBe(3));
    }

    [Fact]
    public void CompareFieldByFieldSavesTheChosenValues()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello", ["summary"] = "Before" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "summary", "Their summary");
        _content.ChangeElsewhere(id, "title", "Theirs");
        Input(app, "title").Input("My title");
        Input(app, "summary").Input("My summary");
        Save(app);
        app.WaitForAssertion(() => Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeTrue());

        Button(app, "Compare field by field").Click();

        var dialog = Dialog(app, "Someone saved this entry first");
        dialog.Find("tr[data-field='title'] input[value='mine']").HasAttribute("checked").ShouldBeTrue();
        dialog.Find("tr[data-field='title'] input[value='mine']").ParentElement!.TextContent.Trim().ShouldStartWith("Your version of Title: ");
        dialog.Find("tr[data-field='title'] input[value='theirs']").ParentElement!.TextContent.Trim().ShouldStartWith("Saved version of Title: ");
        dialog.Find("tr[data-field='title'] input[value='theirs']").Change(true);
        Button(app, "Save the chosen values").Click();

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        _content.Writes[1].IfMatch.ShouldBe("\"3\"");
        (_content.Writes[1].Body["title"]!.GetValue<string>(), _content.Writes[1].Body["summary"]!.GetValue<string>()).ShouldBe(("Theirs", "My summary"));
    }

    [Fact]
    public async Task TheConflictReopenedAfterAnEditComparesTheValuesTheFormHoldsNow()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "summary", "Theirs");
        Input(app, "title").Input("First");
        Save(app);
        var dialog = Dialog(app, "Someone saved this entry first");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());

        // Dismissed by Escape, then the user edits on and opens it again from the problem above the form.
        await app.InvokeAsync(() => dialog.Instance.OpenChanged.InvokeAsync(false));
        Input(app, "title").Input("Second");
        Button(app, "Compare the versions").Click();

        app.WaitForAssertion(() => Rows(Dialog(app, "Someone saved this entry first"))[0].ShouldBe(("Title", "Second", "Hello")));
        Button(app, "Keep mine").Click();
        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        _content.Writes[1].Body["title"]!.GetValue<string>().ShouldBe("Second");
        app.WaitForAssertion(() => Document.Invocations.Count(call => call.Identifier == "focusHeading").ShouldBeGreaterThan(0));
    }

    [Fact]
    public void ASecond412WhileResolvingOpensTheDialogOnTheNewerVersion()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "summary", "Theirs");
        Input(app, "title").Input("Mine");
        Save(app);
        app.WaitForAssertion(() => Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeTrue());
        _content.ChangeElsewhere(id, "slug", "their-slug");

        Button(app, "Keep mine").Click();

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        app.WaitForAssertion(() => Rows(Dialog(app, "Someone saved this entry first")).ShouldBe([("Title", "Mine", "Hello"), ("Slug", "No value", "their-slug")]));
        Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeTrue();
        Button(app, "Keep mine").Click();

        app.WaitForAssertion(() => _content.Version(id).ShouldBe(4));
        _content.Writes[2].IfMatch.ShouldBe("\"3\"");
        (_content.Fields(id)["title"]!.GetValue<string>(), _content.Fields(id)["summary"]!.GetValue<string>(), _content.Fields(id)["slug"]!.GetValue<string>())
            .ShouldBe(("Mine", "Theirs", "their-slug"));
    }

    [Fact]
    public void KeepMineWithNothingOfTheUsersToKeepTakesTheSavedVersionWithoutASave()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        _content.ChangeElsewhere(id, "title", "Same");
        Input(app, "title").Input("Same");
        Save(app);
        app.WaitForAssertion(() => Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeTrue());
        Dialog(app, "Someone saved this entry first").Find("p").TextContent.ShouldNotBeEmpty();

        Button(app, "Keep mine").Click();

        app.WaitForAssertion(() => Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeFalse());
        _content.Writes.Count.ShouldBe(1);
        _content.Version(id).ShouldBe(2);
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void AConfirmedLeaveGoesOnAsTheNavigationItWas()
    {
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        Input(app, "title").Input("Mine");

        Navigation.NavigateTo("not-found", replace: true);
        app.WaitForAssertion(() => Button(app, "Leave").Click());

        app.WaitForAssertion(() => Navigation.Uri.ShouldBe("http://localhost/not-found"));
        var history = ((BunitNavigationManager)Navigation).History.Where(entry => entry.Uri == "not-found").ToList();
        history.ShouldHaveSingleItem().State.ShouldBe(NavigationState.Succeeded);
        history[0].Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Fact]
    public void ATabWhoseFieldsAreAllHiddenIsNotDrawn()
    {
        var more = Field("extra");
        more["display"] = new JsonObject { ["tab"] = "More" };
        more["visibleWhen"] = new JsonObject { ["operator"] = "equal", ["field"] = "kind", ["values"] = new JsonArray("external") };
        Management.SetTypes(MainSite, Type("page", fields: [Field("kind"), more]));
        var app = Load("content/page/new");
        app.FindAll("fluent-tab").ShouldBeEmpty();

        Input(app, "kind").Input("external");

        app.WaitForAssertion(() => app.FindAll("fluent-tab").Select(tab => tab.TextContent.Trim()).ShouldBe(["More"]));
    }

    [Fact]
    public async Task ACancelledSiteSwitchShowsTheSiteBeforeAgainAndAConfirmedOneRemovesTheRestoredDraft()
    {
        Management.AddSite(Shop, "Shop", "shop");
        Management.SetTypes(Shop, Link());
        var id = _content.Add("link", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/link/{id:D}");
        Input(app, "title").Input("Mine");
        // A draft kept for this entry, as an ended session keeps one, and the form unsaved again.
        await app.InvokeAsync(() => Services.GetRequiredService<FormDrafts>().SaveTrackedAsync(SignedInUser));
        Storage[FormDrafts.StorageKey].ShouldNotBeNull();
        Input(app, "title").Input("Mine again");

        app.Find(".site-switcher select").Change(Shop.ToString("D"));
        app.Find(".site-switcher select").GetAttribute("value").ShouldBe(Shop.ToString("D"));
        Dialog(app, "Switch site with unsaved changes?").FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Keep working").Click();

        app.Find(".site-switcher select").GetAttribute("value").ShouldBe(MainSite.ToString("D"));

        app.Find(".site-switcher select").Change(Shop.ToString("D"));
        Dialog(app, "Switch site with unsaved changes?").FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Switch site").Click();

        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Site!.Id.ShouldBe(Shop));
        Storage[FormDrafts.StorageKey].ShouldBeNull();
    }

    private List<bool> UnloadGuards() =>
        [.. Document.Invocations.Where(call => call.Identifier == "guardUnload").Select(call => (bool)call.Arguments[0]!)];

    private async Task Shortcut(FormShortcut shortcut)
    {
        var target = Document.Invocations.Last(call => call.Identifier == "listenForShortcuts").Arguments[0].ShouldBeOfType<DotNetObjectReference<DocumentInterop.ShortcutTarget>>();
        await target.Value.RunAsync(shortcut.ToString().ToLowerInvariant());
    }

    private void Writable(string[]? create = null, string[]? update = null, bool mayUpdate = true)
    {
        var access = Management.Access[MainSite]["types"]!["link"]!.AsObject();
        if (create is not null)
        {
            access["creatableFields"] = new JsonArray([.. create.Select(name => (JsonNode)name)]);
        }

        if (update is not null)
        {
            access["updatableFields"] = new JsonArray([.. update.Select(name => (JsonNode)name)]);
        }

        access["update"] = mayUpdate;
    }

    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => call.Arguments[0]).LastOrDefault() is ElementReference element ? element.Id : null;

    // The input a wrapped text input holds a reference to, which bUnit's markup does not name once it has rendered again.
    private static string? InputReference(IRenderedComponent<AdminApp> app, string label)
    {
        var input = app.FindComponents<CmsTextInput>().Single(found => found.Instance.Label == label).Instance;
        var element = typeof(CmsInputBase<string>).GetProperty("Element", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.GetValue(input);
        return element is ElementReference reference ? reference.Id : null;
    }

    private IRenderedComponent<AdminApp> Load(string route)
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    private static void Save(IRenderedComponent<AdminApp> app) => Button(app, "Save").Click();

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    private static IElement Input(IRenderedComponent<AdminApp> app, string field) => app.Find($"[data-field='{field}'] input");

    private static string Message(IRenderedComponent<AdminApp> app, string field)
    {
        var input = Input(app, field);
        return app.Find($"#{input.GetAttribute("aria-describedby")!.Split(' ')[0]}").TextContent;
    }

    private static (string Field, string Mine, string Theirs)[] Rows(IRenderedComponent<CmsDialog> dialog) =>
    [
        .. dialog.FindAll("tbody tr").Select(row => (row.QuerySelector("th")!.TextContent, row.QuerySelectorAll("td")[0].TextContent, row.QuerySelectorAll("td")[1].TextContent)),
    ];

    // A link: url shows for an external kind, which also requires a summary; a title with a length, a slug with a pattern and
    // a rating with a range.
    private static JsonObject Link()
    {
        var external = new JsonObject { ["operator"] = "equal", ["field"] = "kind", ["values"] = new JsonArray("external") };
        var title = Field("title", required: true);
        title["validation"]!["maxLength"] = 10;
        title["defaultValue"] = "New link";
        var url = Field("url");
        url["visibleWhen"] = external.DeepClone();
        var summary = Field("summary");
        summary["requiredWhen"] = external.DeepClone();
        var slug = Field("slug");
        slug["validation"]!["pattern"] = "^[a-z0-9-]+$";
        var rating = Field("rating", "integer");
        rating["validation"]!["minimum"] = 1;
        rating["validation"]!["maximum"] = 5;
        rating["defaultValue"] = 3;
        return Type("link", fields: [title, Field("kind"), url, summary, slug, rating]);
    }
}
