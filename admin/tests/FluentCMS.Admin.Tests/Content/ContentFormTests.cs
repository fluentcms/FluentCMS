using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Authentication;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

public sealed class ContentFormTests : AdminTestContext
{
    private const string User = "0193a1b2-0000-7000-8000-000000000001";

    private readonly FakeContent _content;

    public ContentFormTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Article(), Type("settings", "singleton", fields: Field("siteName")));
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void TheLayoutFollowsDisplayAndLeavesOutHiddenFields()
    {
        var app = Load("content/article/new");

        app.Find("main h1").TextContent.ShouldBe("Article: new entry");
        FieldsOf(app.Find(".content-form > .content-form-groups")).ShouldBe(["title", "summary", "views", "slug"]);
        app.FindAll("[data-field='secret']").ShouldBeEmpty();
        app.Find("fieldset.content-form-group legend").TextContent.ShouldBe("Address");
        FieldsOf(app.Find("fieldset.content-form-group")).ShouldBe(["slug"]);
        app.FindAll("fluent-tab").Select(tab => tab.TextContent.Trim()).ShouldBe(["Content", "SEO"]);
        FieldsOf(app.Find("#content-form-tab-0-panel")).ShouldBe(["body", "caption"]);
        FieldsOf(app.Find("#content-form-tab-1-panel")).ShouldBe(["seo", "rating"]);
        Wrapper(app, "title").ClassList.ShouldContain("content-form-span-8");
        Wrapper(app, "slug").ClassList.ShouldContain("content-form-span-4");
        Wrapper(app, "summary").ClassList.ShouldContain("content-form-span-12");
    }

    [Fact]
    public void AGeneratedInputHasItsLabelHelpPlaceholderAndAria()
    {
        var app = Load("content/article/new");

        var title = Input(app, "title");
        app.Find($"label[for='{title.Id}']").TextContent.ShouldStartWith("Headline");
        title.GetAttribute("placeholder").ShouldBe("A short title");
        title.GetAttribute("aria-required").ShouldBe("true");
        title.HasAttribute("aria-invalid").ShouldBeFalse();
        app.Find($"#{title.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Shown in lists");
        app.Find($"label[for='{Input(app, "summary").Id}']").TextContent.ShouldBe("Summary");
    }

    [Fact]
    public void AReadOnlyFieldIsTextAndAFieldWithoutAnEditorShowsItsJsonReadOnly()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello", ["views"] = 42, ["seo"] = new JsonObject { ["metaTitle"] = "Meta" } });

        var app = Load($"content/article/{id:D}");

        app.Find("main h1").TextContent.ShouldBe("Hello");
        Wrapper(app, "views").QuerySelectorAll("input, textarea").ShouldBeEmpty();
        Wrapper(app, "views").QuerySelector(".content-text")!.TextContent.ShouldBe("42");
        var json = Wrapper(app, "seo").QuerySelector("pre[role='textbox']")!;
        json.GetAttribute("aria-readonly").ShouldBe("true");
        JsonNode.Parse(json.TextContent)!["metaTitle"]!.GetValue<string>().ShouldBe("Meta");
        var note = json.GetAttribute("aria-describedby")!.Split(' ').Select(described => app.Find($"#{described}").TextContent);
        note.ShouldContain("This field type has no editor in the admin. Its value is shown as JSON and saved unchanged.");
    }

    [Fact]
    public void ANewEntryStartsFromTheDefaultsOfItsFields()
    {
        var app = Load("content/article/new");

        Input(app, "title").GetAttribute("value").ShouldBe("Untitled");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem());
        var create = _content.Writes[0];
        create.Method.ShouldBe(HttpMethod.Post);
        create.Path.ShouldBe("content/article");
        JsonNode.DeepEquals(create.Body, new JsonObject { ["title"] = "Untitled", ["rating"] = 3 }).ShouldBeTrue(create.Body.ToJsonString());
        create.IdempotencyKey.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void TheFirstSaveCreatesAndMovesToTheEntrysRouteKeepingFocusAndTheNextSendsIfMatch()
    {
        var app = Load("content/article/new");
        var focusesBefore = HeadingFocuses();

        Input(app, "title").Input("Hello");
        Save(app);

        app.WaitForAssertion(() => Navigation.Uri.ShouldNotEndWith("/new"));
        app.WaitForAssertion(() => app.Find(".content-form-saved").TextContent.ShouldBe("Saved."));
        var id = Guid.Parse(Navigation.Uri.Split('/')[^1]);
        Navigation.Uri.ShouldBe($"http://localhost/content/article/{id:D}");
        _content.Fields(id)["title"]!.GetValue<string>().ShouldBe("Hello");
        app.Find("main h1").TextContent.ShouldBe("Hello");
        HeadingFocuses().ShouldBe(focusesBefore);

        Input(app, "title").Input("Hello again");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        var save = _content.Writes[1];
        (save.Method, save.Path, save.IfMatch).ShouldBe((HttpMethod.Put, $"content/article/{id:D}", "\"1\""));
        save.Body["title"]!.GetValue<string>().ShouldBe("Hello again");
        _content.Reads.ShouldNotContain(read => read.Contains(id.ToString("D"), StringComparison.Ordinal));
        app.WaitForAssertion(() => _content.Version(id).ShouldBe(2));
    }

    [Fact]
    public void AfterTheMoveToANewEntrysRouteTheNextRouteChangeFocusesTheHeadingAgain()
    {
        var app = Load("content/article/new");
        Save(app);
        app.WaitForAssertion(() => Navigation.Uri.ShouldNotEndWith("/new"));
        var focuses = HeadingFocuses();

        Navigation.NavigateTo("not-found");

        app.WaitForAssertion(() => HeadingFocuses().ShouldBe(focuses + 1));
    }

    [Fact]
    public void ACreateThatEndsAfterTheUserLeftTheFormDoesNotTakeThemBack()
    {
        var app = Load("content/article/new");
        Input(app, "title").Input("Hello");
        Management.Hold = new TaskCompletionSource();
        Save(app);
        Navigation.NavigateTo("not-found");

        Management.Hold.SetResult();

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Post));
        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe("Page not found"));
        Navigation.Uri.ShouldBe("http://localhost/not-found");
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void ARetryAfterTheServerCouldNotBeReachedSendsTheSameKeyAndBodyAndKeepsLaterChanges()
    {
        var app = Load("content/article/new");
        Input(app, "title").Input("First");
        _content.NextWrite = _ => throw new HttpRequestException("The connection closed.");
        Save(app);
        app.WaitForAssertion(() => app.Find(".content-form-problem").TextContent.ShouldContain("could not be reached"));

        Input(app, "title").Input("Second");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        var (lost, retry) = (_content.Writes[0], _content.Writes[1]);
        retry.IdempotencyKey.ShouldBe(lost.IdempotencyKey);
        JsonNode.DeepEquals(retry.Body, lost.Body).ShouldBeTrue();
        retry.Body["title"]!.GetValue<string>().ShouldBe("First");
        app.WaitForAssertion(() => Navigation.Uri.ShouldNotEndWith("/new"));
        Input(app, "title").GetAttribute("value").ShouldBe("Second");
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeTrue();

        Save(app);

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(3));
        (_content.Writes[2].Method, _content.Writes[2].Body["title"]!.GetValue<string>()).ShouldBe((HttpMethod.Put, "Second"));
    }

    [Fact]
    public void SavedIsNoLongerShownOnceTheNextSaveFails()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        Save(app);
        app.WaitForAssertion(() => app.Find(".content-form-saved"));
        _content.ChangeElsewhere(id, "title", "Theirs");

        Save(app);

        app.WaitForAssertion(() => app.Find(".content-form-problem strong").TextContent.ShouldBe("The entry was changed"));
        app.FindAll(".content-form-saved").ShouldBeEmpty();
    }

    [Fact]
    public void AnErrorOnAReadOnlyFieldFocusesItsLabelledGroup()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello", ["views"] = 42 });
        var app = Load($"content/article/{id:D}");
        _content.NextWrite = _ => FakeContent.Invalid(("views", "Not yours to set."));

        Save(app);

        var view = app.FindComponents<ReadOnlyFieldView>().Single();
        app.WaitForAssertion(() => Focused().ShouldBe(Reference(view.Instance, "element")));
        var group = Wrapper(app, "views").QuerySelector("[role='group']")!;
        group.GetAttribute("tabindex").ShouldBe("-1");
        app.Find($"#{group.GetAttribute("aria-labelledby")}").TextContent.ShouldBe("Views");
        app.Find($"#{group.GetAttribute("aria-describedby")}").TextContent.ShouldBe("Not yours to set.");
    }

    [Fact]
    public void A422PutsEachErrorOnTheFieldOfItsPathShowsTheRestAboveTheFormAndFocusesTheFirst()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        _content.NextWrite = _ => FakeContent.Invalid(("seo.metaTitle", "Too long."), ("title", "Too short."), ("", "The entry is not valid."), ("gone[0].text", "Unknown."));

        Save(app);

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("aria-invalid").ShouldBe("true"));
        var title = Input(app, "title");
        title.GetAttribute("aria-describedby")!.Split(' ').Select(described => app.Find($"#{described}").TextContent).ShouldContain("Too short.");
        var seo = Wrapper(app, "seo").QuerySelector("pre")!;
        seo.GetAttribute("aria-invalid").ShouldBe("true");
        Wrapper(app, "seo").TextContent.ShouldContain("Too long.");
        app.FindAll(".content-form-problem li").Select(item => item.TextContent).ShouldBe(["The entry is not valid.", "Unknown."]);
        Focused().ShouldBe(InputReference(app, "Headline"));
    }

    [Fact]
    public void AnErrorOnAFieldOfATabOpensTheTabAndFocusesTheField()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        _content.NextWrite = _ => FakeContent.Invalid(("seo.metaTitle", "Too long."));

        Save(app);

        app.WaitForAssertion(() => app.FindComponent<CmsTabs>().Instance.ActiveTabId.ShouldBe("content-form-tab-1"));
        app.WaitForAssertion(() => Focused().ShouldBe(Reference(app.FindComponents<JsonFieldView>().Single(view => view.Instance.Field.Name == "seo").Instance, "element")));
        app.FindAll(".content-form-problem li").ShouldBeEmpty();
    }

    [Fact]
    public void AnErrorWithoutAFieldFocusesTheProblemAboveTheForm()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        _content.NextWrite = _ => FakeContent.Invalid(("", "The entry is not valid."));

        Save(app);

        app.WaitForAssertion(() => Focused().ShouldBe(Reference(app.FindComponent<ContentForm>().Instance, "summary")));
        app.Find(".content-form-problem li").TextContent.ShouldBe("The entry is not valid.");
    }

    [Fact]
    public void ASaveAHookRejectsShowsItsDetailAboveTheForm()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        _content.NextWrite = _ => ServerProblem(System.Net.HttpStatusCode.UnprocessableEntity, "ACME001", "Rejected by a hook", "Articles are frozen this week.");

        Save(app);

        app.WaitForAssertion(() => app.Find(".content-form-problem li").TextContent.ShouldBe("Articles are frozen this week."));
    }

    [Fact]
    public void A412IsShownAsItsProblemAndReloadReadsTheEntryAgain()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        _content.ChangeElsewhere(id, "title", "Theirs");
        Input(app, "title").Input("Mine");

        Save(app);

        app.WaitForAssertion(() => app.Find(".content-form-problem strong").TextContent.ShouldBe("The entry was changed"));
        app.Find(".content-form-problem").TextContent.ShouldContain("Someone saved the entry after you read it.");
        Button(app, "Reload the entry").Click();
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Theirs"));
        app.FindAll(".content-form-problem").ShouldBeEmpty();
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void AnotherProblemIsShownAsTheAnswer()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        _content.NextWrite = _ => Problem(System.Net.HttpStatusCode.Forbidden, "FCMS2001");

        Save(app);

        app.WaitForAssertion(() => app.Find(".content-form-problem strong").TextContent.ShouldBe("Problem"));
        app.FindAll(".content-form-problem fluent-button").ShouldBeEmpty();
    }

    [Fact]
    public async Task TheDraftOfAnUnsavedFormIsRestoredAfterTheSessionEndedAndIsSavedOnItsBaseVersion()
    {
        var id = _content.Add("article", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/article/{id:D}");
        Input(app, "title").Input("Mine");
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeTrue();

        // What the session does when it ends, before the redirect to sign in.
        await Services.GetRequiredService<FormDrafts>().SaveTrackedAsync(User);

        await using var tab = new Tab(Storage, Management);
        var again = tab.Load($"content/article/{id:D}");
        again.WaitForAssertion(() => Input(again, "title").GetAttribute("value").ShouldBe("Mine"));
        again.Find(".content-form-notice").TextContent.ShouldBe("Your unsaved changes from the last session were restored. Save to keep them.");
        tab.Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeTrue();

        Save(again);

        again.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().IfMatch.ShouldBe("\"1\""));
        _content.Writes[0].Body["title"]!.GetValue<string>().ShouldBe("Mine");
        again.WaitForAssertion(() => Storage[FormDrafts.StorageKey].ShouldBeNull());
        tab.Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void ASingletonIsEditedAtTheRouteOfItsType()
    {
        var id = _content.Add("settings", new JsonObject { ["siteName"] = "Blog" });
        var app = Load("content/settings");

        app.WaitForAssertion(() => Input(app, "siteName").GetAttribute("value").ShouldBe("Blog"));
        app.Find("main h1").TextContent.ShouldBe("Settings");
        Input(app, "siteName").Input("My blog");
        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Path.ShouldBe($"content/settings/{id:D}"));
        Navigation.Uri.ShouldBe("http://localhost/content/settings");
    }

    [Fact]
    public void ASingletonWithoutItsEntryIsCreatedAndStaysAtItsRoute()
    {
        var app = Load("content/settings");
        Input(app, "siteName").Input("My blog");

        Save(app);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Post));
        app.WaitForAssertion(() => app.Find(".content-form-saved"));
        Navigation.Uri.ShouldBe("http://localhost/content/settings");
    }

    [Fact]
    public void TheRouteOfACollectionLeadsToANewEntry()
    {
        var app = Load("content/article");

        app.Find("main h1").TextContent.ShouldBe("Article");
        app.FindAll("main fluent-button").Single(button => button.TextContent.Trim() == "New").Click();

        Navigation.Uri.ShouldEndWith("content/article/new");
    }

    [Theory]
    [InlineData("content/nothing/new", "Content type not found")]
    [InlineData("content/article/0193a1b2-0000-7000-8000-00000000ffff", "Entry not found")]
    public void AnUnknownTypeOrEntryIsSaid(string route, string heading)
    {
        var app = Load(route);

        app.WaitForAssertion(() => app.Find("main h1").TextContent.ShouldBe(heading));
        app.FindAll("fluent-button").Where(button => button.TextContent.Trim() == "Save").ShouldBeEmpty();
    }

    private int HeadingFocuses() => Document.Invocations.Count(call => call.Identifier == "focusHeading");

    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => call.Arguments[0]).LastOrDefault() is ElementReference element ? element.Id : null;

    // The element a component holds a reference to, which bUnit's markup does not name once the element has rendered again.
    private static string? Reference(object component, string member)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        for (var type = component.GetType(); type is not null; type = type.BaseType)
        {
            var value = type.GetField(member, Flags)?.GetValue(component) ?? type.GetProperty(member, Flags)?.GetValue(component);
            if (value is ElementReference element)
            {
                return element.Id;
            }
        }

        return null;
    }

    private static string? InputReference(IRenderedComponent<AdminApp> app, string label) =>
        Reference(app.FindComponents<CmsTextInput>().Single(input => input.Instance.Label == label).Instance, "Element");

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

    private static IElement Wrapper(IRenderedComponent<AdminApp> app, string field) => app.Find($"[data-field='{field}']");

    private static IElement Input(IRenderedComponent<AdminApp> app, string field) => app.Find($"[data-field='{field}'] input");

    private static string[] FieldsOf(IElement container) =>
        [.. container.QuerySelectorAll("[data-field]").Select(field => field.GetAttribute("data-field")!)];

    private static JsonObject Article() => Type(
        "article",
        fields:
        [
            Described("title", "text", new JsonObject { ["label"] = "Headline", ["help"] = "Shown in lists", ["placeholder"] = "A short title", ["width"] = 8 }, required: true, defaultValue: "Untitled"),
            Described("summary", "text"),
            Described("secret", "text", hidden: true, defaultValue: "kept out"),
            Described("views", "integer", readOnly: true, defaultValue: 0),
            Described("body", "richText", new JsonObject { ["tab"] = "Content" }),
            Described("slug", "text", new JsonObject { ["group"] = "Address", ["width"] = 4 }),
            Described("seo", "component", new JsonObject { ["tab"] = "SEO" }),
            Described("caption", "text", new JsonObject { ["tab"] = "Content", ["width"] = 0 }),
            Described("rating", "integer", new JsonObject { ["tab"] = "SEO" }, defaultValue: 3),
        ]);

    private static JsonObject Described(string name, string type, JsonObject? display = null, bool required = false, bool hidden = false, bool readOnly = false, JsonNode? defaultValue = null)
    {
        var field = Field(name, type, required);
        field["display"] = display;
        field["hidden"] = hidden;
        field["readOnly"] = readOnly;
        field["defaultValue"] = defaultValue;
        return field;
    }

    // A new page load of the same tab after a new sign-in: its sessionStorage and server, its own services and renderer.
    private sealed class Tab : AdminTestContext
    {
        public Tab(MemorySessionStorage storage, FakeManagementServer management)
        {
            UseNeutralResources();
            Storage = storage;
            Management = management;
            Start();
        }

        public IRenderedComponent<AdminApp> Load(string route)
        {
            Services.GetRequiredService<NavigationManager>().NavigateTo(route);
            var app = Render<AdminApp>();
            app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
            return app;
        }
    }
}
