using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The field actions of the admin halves in the form (A-38, spec 5.1), through the sample half: the menu of a field whose type
// an enabled half's action names, for a caller who holds its permission and on the fields its FieldsFrom names; the half
// loaded when the menu first opens; and a result that goes into the draft as an unsaved change, or leaves it as it was.
public sealed class FieldActionTests : AdminTestContext
{
    private const string Sample = "FluentCMS.Admin.Plugins.Sample.";
    private const string Fields = "reviews/fields";
    private const string FieldsPath = "/api/management/v1/" + Fields;

    private readonly FakeContent _content;
    private readonly SampleLoader _loader = new();
    private readonly List<(string Query, string? Site, string? Locale)> _fieldReads = [];
    private readonly Guid _review;
    private readonly Guid _other;

    public FieldActionTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Type("review", fields: [Field("title"), Field("body", "longText"), Field("rating", "integer")]));
        _review = _content.Add("review", new JsonObject { ["title"] = "great", ["body"] = "A fine place.", ["rating"] = 4 });
        _other = _content.Add("review", new JsonObject { ["title"] = "poor", ["body"] = "Cold soup.", ["rating"] = 1 });
        var content = Management.Respond!;
        Management.Respond = (request, path, body) => path == Fields ? FieldsAnswer(request) : content(request, path, body);
    }

    // What GET reviews/fields answers, in the place of a half's own read such as /ai/fields.
    private Func<HttpResponseMessage> FieldsResponse { get; set; } = () => Json(new JsonObject { ["fields"] = new JsonObject { ["review"] = new JsonArray("body") } });

    [Fact]
    public void TheSampleHalfsActionShowsOnItsFieldTypesAndItsHalfLoadsWhenTheMenuFirstOpens()
    {
        var app = LoadReview(Reviews);

        Toggles(app).ShouldBe(["title", "body"]);
        _loader.Loads.ShouldBe(0);

        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Shout"]));
        _loader.Loads.ShouldBe(1);
        Toggle(app, "title").GetAttribute("aria-expanded").ShouldBe("true");
        Form(app).HasUnsavedChanges.ShouldBeFalse();

        ActionButton(app, "title", "Shout").Click();

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("GREAT"));
        Form(app).HasUnsavedChanges.ShouldBeTrue();
        app.WaitForAssertion(() => Announced(app).ShouldContain("Shout changed Title. The change is not saved yet."));
        _content.Writes.ShouldBeEmpty();

        Open(app, "body");
        app.WaitForAssertion(() => ActionButtons(app, "body").ShouldBe(["Shout"]));
        _loader.Loads.ShouldBe(1);

        Button(app, "Save").Click();
        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Body["title"]!.GetValue<string>().ShouldBe("GREAT"));
    }

    [Fact]
    public void AnActionThatAsksIsConfirmedFirstAndItsCancelLeavesTheDraft()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("ClearFieldAction", "clear")] });
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Clear"]));

        ActionButton(app, "title", "Clear").Click();
        app.WaitForAssertion(() => Confirmation(app).Instance.Open.ShouldBeTrue());
        Confirmation(app).Find("p").TextContent.ShouldBe("Clear title?");
        DialogButton(Confirmation(app), "Cancel").Click();

        app.WaitForAssertion(() => app.FindComponents<CmsDialog>().ShouldNotContain(found => found.Instance.Open));
        Input(app, "title").GetAttribute("value").ShouldBe("great");
        Form(app).HasUnsavedChanges.ShouldBeFalse();

        ActionButton(app, "title", "Clear").Click();
        app.WaitForAssertion(() => Confirmation(app).Instance.Open.ShouldBeTrue());
        DialogButton(Confirmation(app), "Clear").Click();

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBeNullOrEmpty());
        Form(app).HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    public void AnActionThatThrowsShowsAProblemBesideTheFieldAndLeavesTheDraft()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("ThrowingFieldAction", "throw")] });
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Throw"]));

        ActionButton(app, "title", "Throw").Click();

        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-problem")!.TextContent.ShouldBe("The action failed. The browser's console has the cause."));
        Input(app, "title").GetAttribute("value").ShouldBe("great");
        Form(app).HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public void AnActionThatDoesNotApplyToTheFieldIsLeftOutAndEscapeClosesTheList()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("NeverFieldAction", "never")] });
        Open(app, "title");

        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-list p")!.TextContent.ShouldBe("No action applies to this field as it is now."));
        ActionButtons(app, "title").ShouldBeEmpty();

        Wrapper(app, "title").QuerySelector(".field-actions-list")!.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-list").ShouldBeNull());
        Toggle(app, "title").GetAttribute("aria-expanded").ShouldBe("false");
        app.WaitForAssertion(() => Focused().ShouldBe(ToggleReference(app, "Title")));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ACallerWithoutThePermissionOrASiteWithThePluginOffSeesNoActionAndTheHalfIsNotLoaded(bool permitted, bool enabled)
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", permission: "reviews.notes.manage")] }, permitted, enabled);

        Toggles(app).ShouldBeEmpty();
        _loader.Loads.ShouldBe(0);
        _fieldReads.ShouldBeEmpty();
    }

    // Read once for the form, whichever of its fields the action is on.
    [Fact]
    public void FieldsFromNamesTheFieldsBeforeTheHalfLoadsAndIsReadOnce()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });

        app.WaitForAssertion(() => Toggles(app).ShouldBe(["body"]));
        _fieldReads.ShouldHaveSingleItem();
        _loader.Loads.ShouldBe(0);
    }

    [Theory]
    [InlineData("assets", """{ "assets": true }""", new[] { "title", "body" })]
    [InlineData("assets", """{ "assets": false }""", new string[0])]
    [InlineData("fields", """{ "fields": [] }""", new string[0])]
    [InlineData("fields", null, new string[0])]
    public void AMemberOfTrueOffersEveryFieldAndAnyOtherAnswerOrARefusalNone(string member, string? answer, string[] offered)
    {
        FieldsResponse = () => answer is null ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : Json(JsonNode.Parse(answer)!);

        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: $"{Fields}#{member}")] });

        app.WaitForAssertion(() => _fieldReads.ShouldHaveSingleItem());
        app.WaitForAssertion(() => Toggles(app).ShouldBe(offered));
        _loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void AFieldTheCallerMayNotChangeHasNoActions()
    {
        Management.Access[MainSite]["types"]!["review"]!["updatableFields"] = new JsonArray("body", "rating");

        var app = LoadReview(Reviews);

        Toggles(app).ShouldBe(["body"]);
    }

    // The form shows at once, and adds the actions FieldsFrom gates once the read answers. Nothing is kept: the next entry
    // reads again, so that what the site opted in since is offered without a reload.
    [Fact]
    public void TheFormDoesNotWaitForFieldsFromAndReadsItAgainForTheNextEntry()
    {
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.StartsWith(FieldsPath, StringComparison.Ordinal) ? held.Task : null;

        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });

        Toggles(app).ShouldBeEmpty();
        held.SetResult();
        app.WaitForAssertion(() => Toggles(app).ShouldBe(["body"]));

        FieldsResponse = () => Json(new JsonObject { ["fields"] = new JsonObject { ["review"] = new JsonArray("title") } });
        Navigate($"content/review/{_other:D}");
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("poor"));
        app.WaitForAssertion(() => Toggles(app).ShouldBe(["title"]));
        _fieldReads.Count.ShouldBe(2);
    }

    // A read of the entry before, held until the next entry has its own answer, never offers its answer there: the load of
    // the next entry cancels it.
    [Fact]
    public async Task AFieldsFromReadOfTheEntryBeforeNeverReachesTheNextEntry()
    {
        var held = HoldFirstFieldsRead();
        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });
        Toggles(app).ShouldBeEmpty();

        Navigate($"content/review/{_other:D}");
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("poor"));
        app.WaitForAssertion(() => Toggles(app).ShouldBe(["body"]));
        held.SetResult();
        await SettledAsync(app);

        _fieldReads.ShouldHaveSingleItem();
        Toggles(app).ShouldBe(["body"]);
    }

    // The same across the end of the form: a read of a form that went never reaches the next form.
    [Fact]
    public async Task AFieldsFromReadOfAFormThatWentNeverReachesTheNextForm()
    {
        var held = HoldFirstFieldsRead();
        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });
        Toggles(app).ShouldBeEmpty();

        Navigate("content/review");
        app.WaitForAssertion(() => app.FindComponents<ContentForm>().ShouldBeEmpty());
        Navigate($"content/review/{_other:D}");
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("poor"));
        app.WaitForAssertion(() => Toggles(app).ShouldBe(["body"]));
        held.SetResult();
        await SettledAsync(app);

        _fieldReads.ShouldHaveSingleItem();
        Toggles(app).ShouldBe(["body"]);
    }

    // The form reads FieldsFrom again in the locale it shows next, and in its site.
    [Fact]
    public void ALocaleSwitchReadsFieldsFromAgainInTheNewLocale()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var review = Type("review", fields: [Field("title"), Field("body", "longText"), Field("rating", "integer")]);
        review["localized"] = true;
        Management.SetTypes(MainSite, review);
        _content.Add("review", new JsonObject { ["title"] = "génial", ["body"] = "Un bel endroit.", ["rating"] = 4 }, id: _review, locale: "fr");

        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });
        app.WaitForAssertion(() => Toggles(app).ShouldBe(["body"]));
        FieldsResponse = () => Json(new JsonObject { ["fields"] = new JsonObject { ["review"] = new JsonArray("title") } });

        Navigate($"content/review/{_review:D}?locale=fr");

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("génial"));
        app.WaitForAssertion(() => Toggles(app).ShouldBe(["title"]));
        // The site's default locale goes as none, as the form's own reads send it.
        _fieldReads.Select(read => (read.Site, read.Locale)).ShouldBe([(MainSite.ToString("D"), null), (MainSite.ToString("D"), "fr")]);
    }

    [Fact]
    public void ACallerWhoMayNotChangeTheEntryReadsNoFieldsFrom()
    {
        Management.Access[MainSite]["types"]!["review"]!["update"] = false;

        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });

        Toggles(app).ShouldBeEmpty();
        _fieldReads.ShouldBeEmpty();
    }

    // A source the client would refuse, as one that leads out of the Management API or does not resolve under it, or one with
    // a dot segment, offers the action on no field and is never sent; the form works without it.
    [Theory]
    [InlineData("../reviews/fields#fields")]
    [InlineData("reviews/%2E%2E/../x#fields")]
    [InlineData("file:x#fields")]
    public void AFieldsFromTheClientWouldRefuseOffersNothingAndTheFormStillWorks(string fieldsFrom)
    {
        var log = new OffersLog();
        Services.AddSingleton<ILogger<FieldActionOffers>>(log);

        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: fieldsFrom), Declared("ClearFieldAction", "clear")] });

        app.WaitForAssertion(() => Toggles(app).ShouldBe(["title", "body"]));
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Clear"]));
        Management.Requests.Select(request => request.Path).Distinct().ShouldBeSubsetOf(["me", "me/sites", "me/preferences", "me/access", "plugins", "schema", $"content/review/{_review:D}"]);
        log.Lines.ShouldBeEmpty();
    }

    // The result of an action asked on one entry never reaches the next entry the form shows.
    [Fact]
    public async Task AResultThatComesAfterTheFormLoadedAnotherEntryIsDropped()
    {
        HeldFieldAction.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HeldFieldAction.Answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = LoadReview(Reviews with { FieldActions = [Declared("HeldFieldAction", "held")] });
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Held"]));
        ActionButton(app, "title", "Held").Click();

        Services.GetRequiredService<NavigationManager>().NavigateTo($"content/review/{_other:D}");
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("poor"));
        HeldFieldAction.Release.SetResult();
        await HeldFieldAction.Answered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // The menu's continuation after the action is posted to the renderer, which these calls let run first.
        await app.InvokeAsync(() => { });
        await app.InvokeAsync(() => { });
        Announced(app).ShouldNotContain(text => text.StartsWith("Held changed", StringComparison.Ordinal));
        Input(app, "title").GetAttribute("value").ShouldBe("poor");
        Form(app).HasUnsavedChanges.ShouldBeFalse();
    }

    [Fact]
    public void AResultThatDoesNotFitTheFieldsTypeIsRefusedAndLeavesTheDraft()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("ObjectFieldAction", "object")] });
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Object"]));

        ActionButton(app, "title", "Object").Click();

        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-problem")!.TextContent.ShouldBe("The action gave a value this field does not take. The field is unchanged."));
        Input(app, "title").GetAttribute("value").ShouldBe("great");
        Form(app).HasUnsavedChanges.ShouldBeFalse();
        Announced(app).ShouldContain("The action gave a value this field does not take. The field is unchanged.");
    }

    // Escape closes the list from its button too, and what the list says without actions is announced.
    [Fact]
    public void EscapeOnTheButtonClosesTheListAndNoActionIsAnnounced()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("NeverFieldAction", "never")] });
        Open(app, "title");
        app.WaitForAssertion(() => Announced(app).ShouldContain("No action applies to this field as it is now."));
        Announced(app).ShouldContain("Loading the actions…");

        Toggle(app, "title").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-list").ShouldBeNull());
        app.WaitForAssertion(() => Focused().ShouldBe(ToggleReference(app, "Title")));
    }

    // The list's loading text is announced once, by the shell's live region and not by a region of the list's own, and not
    // again when the list opens a second time.
    [Fact]
    public void TheLoadingOfTheActionsIsAnnouncedOnce()
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _loader.Gate = loaded.Task;
        var app = LoadReview(Reviews);

        Open(app, "title");

        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-list p")!.TextContent.ShouldBe("Loading the actions…"));
        app.WaitForAssertion(() => LiveRegionsSaying(app, "Loading the actions…").ShouldBe(1));
        loaded.SetResult();
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Shout"]));

        Open(app, "title");
        app.WaitForAssertion(() => Wrapper(app, "title").QuerySelector(".field-actions-list").ShouldBeNull());
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Shout"]));
        Announced(app).Count(text => text == "Loading the actions…").ShouldBe(1);
    }

    // An action that throws ObjectDisposedException once its menu went with the entry was stopped, not failed: nothing of it
    // is shown or announced.
    [Fact]
    public async Task AnActionThatFindsItsFormGoneIsNotAFailure()
    {
        DisposedFieldAction.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DisposedFieldAction.Thrown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var app = LoadReview(Reviews with { FieldActions = [Declared("DisposedFieldAction", "disposed")] });
        Open(app, "title");
        app.WaitForAssertion(() => ActionButtons(app, "title").ShouldBe(["Disposed"]));
        ActionButton(app, "title", "Disposed").Click();

        Navigate($"content/review/{_other:D}");
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("poor"));
        DisposedFieldAction.Release.SetResult();
        await DisposedFieldAction.Thrown.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        // The menu's continuation after the action is posted to the renderer, which these calls let run first.
        await app.InvokeAsync(() => { });
        await app.InvokeAsync(() => { });
        Announced(app).ShouldNotContain("The action failed. The browser's console has the cause.");
    }

    // The shape a field action's value must have for the field: a field of several takes an array of values of its type, and
    // a number field a number.
    [Theory]
    [InlineData("text", false, "\"a\"", true)]
    [InlineData("text", false, "[\"a\"]", false)]
    [InlineData("text", true, "[\"a\", \"b\"]", true)]
    [InlineData("text", true, "\"a\"", false)]
    [InlineData("text", true, "[\"a\", 1]", false)]
    [InlineData("text", true, "[\"a\", null]", false)]
    [InlineData("integer", false, "4", true)]
    [InlineData("integer", false, "\"4\"", false)]
    [InlineData("integer", true, "[4, 5]", true)]
    [InlineData("integer", true, "4", false)]
    [InlineData("decimal", false, "4.5", true)]
    [InlineData("decimal", false, "\"4.5\"", false)]
    [InlineData("decimal", false, "true", false)]
    [InlineData("boolean", false, "false", true)]
    [InlineData("boolean", false, "\"false\"", false)]
    [InlineData("relation", false, "{ \"id\": 1 }", true)]
    [InlineData("integer", false, "null", true)]
    public void AValueFitsAFieldOnlyInTheShapeOfItsType(string type, bool multiple, string json, bool fits) =>
        FieldValues.Fits(new SchemaFieldDescription { Name = "value", Type = type, Multiple = multiple, Validation = new() }, JsonNode.Parse(json)).ShouldBe(fits);

    private static AdminPluginManifest Reviews => AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));

    private static AdminManifestFieldAction Declared(string type, string name, string? permission = null, string? fieldsFrom = null) =>
        new() { Type = Sample + type, Name = name, FieldTypes = ["text", "longText"], Permission = permission, FieldsFrom = fieldsFrom };

    private HttpResponseMessage FieldsAnswer(HttpRequestMessage request)
    {
        lock (_fieldReads)
        {
            _fieldReads.Add((request.RequestUri!.Query, Header(request, "X-Cms-Site"), Header(request, "X-Cms-Locale")));
        }

        return FieldsResponse();

        static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }

    // Holds the first read of FieldsFrom until the test completes the task; the first read to answer opts body in, a later
    // one title.
    private TaskCompletionSource HoldFirstFieldsRead()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        Management.HoldPath = path => path.StartsWith(FieldsPath, StringComparison.Ordinal) && Interlocked.Increment(ref reads) == 1 ? held.Task : null;
        FieldsResponse = () => Json(new JsonObject { ["fields"] = new JsonObject { ["review"] = new JsonArray(_fieldReads.Count == 1 ? "body" : "title") } });
        return held;
    }

    // Lets a released read answer, unless it was cancelled, and the form render what it gives.
    private async Task SettledAsync(IRenderedComponent<AdminApp> app)
    {
        SpinWait.SpinUntil(() => _fieldReads.Count > 1, TimeSpan.FromSeconds(1));
        await app.InvokeAsync(() => { });
        await app.InvokeAsync(() => { });
    }

    private void Navigate(string route) => Services.GetRequiredService<NavigationManager>().NavigateTo(route);

    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => call.Arguments[0]).LastOrDefault() is ElementReference element ? element.Id : null;

    // The element of the button that shows and hides the actions of the field with the label.
    private static string ToggleReference(IRenderedComponent<AdminApp> app, string label) =>
        app.FindComponents<FieldActionMenu>().Single(menu => menu.Instance.FieldLabel == label).FindComponents<CmsButton>()[0].FindComponent<FluentButton>().Instance.Element.Id;

    // The live regions whose text holds the text: each would read it out.
    private static int LiveRegionsSaying(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("[role=status], [role=alert], [aria-live]").Count(region => region.TextContent.Contains(text, StringComparison.Ordinal));

    private IRenderedComponent<AdminApp> LoadReview(AdminPluginManifest half, bool permitted = true, bool enabled = true)
    {
        Services.AddSingleton(new AdminPluginCatalog([half], []));
        Services.AddScoped<IAdminAssemblyLoader>(_ => _loader);
        if (!permitted)
        {
            Management.Grant(MainSite, ["schema.read"]);
        }

        Management.Plugins[MainSite] = [Plugin("reviews", enabled)];
        Start();
        Navigate($"content/review/{_review:D}");
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => Services.GetRequiredService<AdminContext>().Status.ShouldBe(ShellStatus.Ready));
        app.WaitForAssertion(() => Input(app, "title"));
        return app;
    }

    private static ContentForm Form(IRenderedComponent<AdminApp> app) => app.FindComponent<ContentForm>().Instance;

    private static IElement Wrapper(IRenderedComponent<AdminApp> app, string field) => app.Find($"[data-field='{field}']");

    private static IElement Input(IRenderedComponent<AdminApp> app, string field) => app.Find($"[data-field='{field}'] input, [data-field='{field}'] textarea");

    private static string[] Toggles(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[data-field]").Where(field => field.QuerySelector(".field-actions") is not null).Select(field => field.GetAttribute("data-field")!)];

    private static IElement Toggle(IRenderedComponent<AdminApp> app, string field) => Wrapper(app, field).QuerySelector(".field-actions > fluent-button")!;

    private static void Open(IRenderedComponent<AdminApp> app, string field) => Toggle(app, field).Click();

    private static string[] ActionButtons(IRenderedComponent<AdminApp> app, string field) =>
        [.. Wrapper(app, field).QuerySelectorAll(".field-actions-list fluent-button").Select(button => button.TextContent.Trim())];

    private static IElement ActionButton(IRenderedComponent<AdminApp> app, string field, string text) =>
        Wrapper(app, field).QuerySelectorAll(".field-actions-list fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IRenderedComponent<CmsDialog> Confirmation(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == "Clear");

    private static IElement DialogButton(IRenderedComponent<CmsDialog> dialog, string text) =>
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static List<string> Announced(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[role=status] > div").Select(message => message.TextContent)];

    private sealed class SampleLoader : IAdminAssemblyLoader
    {
        private int _loads;

        public int Loads => _loads;

        // Ends a load when it ends; at once by default.
        public Task Gate { get; set; } = Task.CompletedTask;

        public async Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loads);
            await Gate.WaitAsync(cancellationToken);
            return typeof(ReviewsAdminPlugin).Assembly;
        }
    }

    // What the form's reads of FieldsFrom log.
    private sealed class OffersLog : ILogger<FieldActionOffers>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines)
            {
                Lines.Add(formatter(state, exception));
            }
        }
    }
}
