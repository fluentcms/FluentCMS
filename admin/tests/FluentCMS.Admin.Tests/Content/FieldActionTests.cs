using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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

    private readonly FakeContent _content;
    private readonly SampleLoader _loader = new();
    private readonly List<(string Query, string? Locale)> _fieldReads = [];
    private readonly Guid _review;

    public FieldActionTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Type("review", fields: [Field("title"), Field("body", "longText"), Field("rating", "integer")]));
        _review = _content.Add("review", new JsonObject { ["title"] = "great", ["body"] = "A fine place.", ["rating"] = 4 });
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

    [Fact]
    public void FieldsFromNamesTheFieldsBeforeTheHalfLoadsAndIsReadOnce()
    {
        var app = LoadReview(Reviews with { FieldActions = [Declared("ShoutAction", "shout", fieldsFrom: Fields + "#fields")] });

        Toggles(app).ShouldBe(["body"]);
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

        Toggles(app).ShouldBe(offered);
        _loader.Loads.ShouldBe(0);
    }

    [Fact]
    public void AFieldTheCallerMayNotChangeHasNoActions()
    {
        Management.Access[MainSite]["types"]!["review"]!["updatableFields"] = new JsonArray("body", "rating");

        var app = LoadReview(Reviews);

        Toggles(app).ShouldBe(["body"]);
    }

    private static AdminPluginManifest Reviews => AdminPluginManifestJson.Read(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName)));

    private static AdminManifestFieldAction Declared(string type, string name, string? permission = null, string? fieldsFrom = null) =>
        new() { Type = Sample + type, Name = name, FieldTypes = ["text", "longText"], Permission = permission, FieldsFrom = fieldsFrom };

    private HttpResponseMessage FieldsAnswer(HttpRequestMessage request)
    {
        _fieldReads.Add((request.RequestUri!.Query, request.Headers.TryGetValues("X-Cms-Locale", out var locales) ? locales.Single() : null));
        return FieldsResponse();
    }

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
        Services.GetRequiredService<NavigationManager>().NavigateTo($"content/review/{_review:D}");
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

        public Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loads);
            return Task.FromResult(typeof(ReviewsAdminPlugin).Assembly);
        }
    }
}
