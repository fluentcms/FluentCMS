using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The status controls of the form (A-16): save draft, publish, unpublish and discard shown by the status of the locale and
// the caller's access, the status badge, the warnings of a publish and its 422, and the questions before discard and unpublish.
public sealed class ContentFormStatusTests : AdminTestContext
{
    private readonly FakeContent _content;

    public ContentFormStatusTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Post());
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Theory]
    [InlineData("draft", new[] { "Save", "Publish" }, new[] { "en Draft" })]
    [InlineData("published", new[] { "Save", "Unpublish" }, new[] { "en Published" })]
    [InlineData("modified", new[] { "Save", "Publish", "Discard changes", "Unpublish" }, new[] { "en Modified" })]
    [InlineData("unpublished", new[] { "Save", "Publish" }, new[] { "en Unpublished" })]
    public void TheControlsAndTheBadgeFollowTheStatusOfTheLocale(string status, string[] controls, string[] badges)
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: status, published: new JsonObject { ["title"] = "Before" });

        var app = Load($"content/post/{id:D}");

        Controls(app).ShouldBe(controls);
        Badges(app).ShouldBe(badges);
    }

    [Fact]
    public void ANewEntryOffersSaveAndPublishWithoutABadge()
    {
        var app = Load("content/post/new");

        Controls(app).ShouldBe(["Save", "Publish"]);
        app.FindAll(".content-badge").ShouldBeEmpty();
    }

    [Fact]
    public void UnsavedChangesOfAPublishedEntryOfferPublishAndDiscard()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Load($"content/post/{id:D}");

        Input(app, "title").Input("Changed");

        app.WaitForAssertion(() => Controls(app).ShouldBe(["Save", "Publish", "Discard changes", "Unpublish"]));
    }

    [Theory]
    [InlineData("publish", new[] { "Save", "Discard changes", "Unpublish" })]
    [InlineData("unpublish", new[] { "Save", "Publish", "Discard changes" })]
    [InlineData("update", new[] { "Publish", "Unpublish" })]
    public void AControlIsAbsentWhenTheCallerMayNotDoIt(string denied, string[] controls)
    {
        Management.Access[MainSite]["types"]!["post"]![denied] = false;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "modified", published: new JsonObject { ["title"] = "Before" });

        var app = Load($"content/post/{id:D}");

        Controls(app).ShouldBe(controls);
    }

    // Create and publish grants whose patterns name some types only, which /me/access answers type by type.
    [Theory]
    [InlineData("post", new[] { "Save", "Publish" })]
    [InlineData("note", new[] { "Save" })]
    [InlineData("memo", new string[0])]
    public void ANewEntryIsOfferedWhatTheGrantsGiveForItsType(string type, string[] controls)
    {
        Management.SetTypes(MainSite, Post(), Type("note", fields: [Field("title")]), Type("memo", fields: [Field("title")]));
        Management.Access[MainSite]["types"]!["note"]!["publish"] = false;
        Management.Access[MainSite]["types"]!["memo"]!["create"] = false;
        Management.Access[MainSite]["types"]!["memo"]!["publish"] = false;

        var app = Load($"content/{type}/new");

        Controls(app).ShouldBe(controls);
    }

    [Fact]
    public async Task ACallerWhoMayNotPublishHasNoPublishShortcut()
    {
        Management.Access[MainSite]["types"]!["post"]!["publish"] = false;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        await Shortcut(FormShortcut.Publish);
        await Shortcut(FormShortcut.Help);

        app.WaitForAssertion(() => Dialog(app, "Keyboard shortcuts").FindAll("dd").Select(item => item.TextContent).ShouldNotContain("Publish the entry"));
        _content.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void PublishSendsTheTagOfTheReadAndShowsTheEntryAsAnswered()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => Badges(app).ShouldBe(["en Published"]));
        var publish = _content.Writes.ShouldHaveSingleItem();
        // In the site's default locale, which the form names none for, as its read and its save.
        (publish.Method, publish.Path, publish.IfMatch, publish.Locale).ShouldBe((HttpMethod.Post, $"content/post/{id:D}/publish", "\"1\"", null));
        Controls(app).ShouldBe(["Save", "Unpublish"]);
        app.Find(".content-form-saved").TextContent.ShouldBe("Published.");
        Announced(app).ShouldContain("Published.");
        app.WaitForAssertion(() => HeadingFocuses().ShouldBeGreaterThan(0));
    }

    [Fact]
    public void PublishSavesUnsavedChangesFirstAndPublishesOnTheTagOfThatSave()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Load($"content/post/{id:D}");
        Input(app, "title").Input("Changed");

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        var (save, publish) = (_content.Writes[0], _content.Writes[1]);
        (save.Method, save.IfMatch, save.Body["title"]!.GetValue<string>()).ShouldBe((HttpMethod.Put, "\"1\"", "Changed"));
        (publish.Path, publish.IfMatch).ShouldBe(($"content/post/{id:D}/publish", "\"2\""));
        app.WaitForAssertion(() => Badges(app).ShouldBe(["en Published"]));
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();
    }

    [Fact]
    public void PublishOfANewEntryCreatesItThenPublishesIt()
    {
        var app = Load("content/post/new");
        Input(app, "title").Input("Fresh");

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        _content.Writes[0].Method.ShouldBe(HttpMethod.Post);
        var id = Guid.Parse(_content.Writes[1].Path.Split('/')[2]);
        _content.Writes[1].Path.ShouldBe($"content/post/{id:D}/publish");
        app.WaitForAssertion(() => Navigation.Uri.ShouldBe($"http://localhost/content/post/{id:D}"));
        app.WaitForAssertion(() => Badges(app).ShouldBe(["en Published"]));
    }

    [Fact]
    public void APublishWithARequiredFieldEmptyShowsItOnTheFieldAndSendsNothing()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "" });
        var app = Load($"content/post/{id:D}");

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => app.Find(".content-form-problem").TextContent.ShouldContain("The entry was not published. Correct the problems shown"));
        Message(app, "title").ShouldBe("A value is required to publish.");
        _content.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void TheWarningsOfAPublishAreListedWithLinksToTheEntries()
    {
        var author = Guid.CreateVersion7();
        _content.Warnings =
        [
            new JsonObject { ["path"] = "author", ["code"] = "FCMS2403", ["message"] = "The author Ada is not published.", ["targetType"] = "author", ["targetId"] = author },
            new JsonObject { ["path"] = "related", ["code"] = "FCMS2404", ["message"] = "A related entry cannot be reached." },
        ];
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => app.Find(".content-form-warnings"));
        var items = app.FindAll(".content-form-warnings li");
        items.Select(item => item.TextContent.Trim()).ShouldBe(["The author Ada is not published.", "A related entry cannot be reached."]);
        items[0].QuerySelector("a")!.GetAttribute("href").ShouldBe($"content/author/{author:D}");
        items[1].QuerySelector("a").ShouldBeNull();
        app.Find(".content-form-saved").TextContent.ShouldBe("Published, with warnings about its references.");
        Badges(app).ShouldBe(["en Published"]);

        // The warnings belong to that publish, and go with the next change.
        Input(app, "title").Input("Changed");
        app.WaitForAssertion(() => app.FindAll(".content-form-warnings").ShouldBeEmpty());
    }

    [Fact]
    public void A422OfABlockingTypeShowsAsErrorsOnTheFields()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        _content.NextWrite = _ => FakeContent.Invalid(("author", "The author Ada is not published."));

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => Message(app, "author").ShouldBe("The author Ada is not published."));
        app.Find(".content-form-problem").TextContent.ShouldContain("The entry was not published. Correct the problems shown");
        Badges(app).ShouldBe(["en Draft"]);
    }

    [Fact]
    public void DiscardAsksFirstAndThenShowsThePublishedVersion()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Draft title" }, version: 3, status: "modified", published: new JsonObject { ["title"] = "Published title" });
        var app = Load($"content/post/{id:D}");
        Input(app, "title").Input("Unsaved");

        ToolbarButton(app, "Discard changes").Click();

        var dialog = Dialog(app, "Discard the changes?");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        _content.Writes.ShouldBeEmpty();
        dialog.FindAll("fluent-button").Select(button => button.TextContent.Trim()).Where(text => text.Length > 0).ShouldBe(["Keep the changes", "Discard changes"]);
        DialogButton(dialog, "Keep the changes").Click();
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeFalse());
        _content.Writes.ShouldBeEmpty();
        Input(app, "title").GetAttribute("value").ShouldBe("Unsaved");

        ToolbarButton(app, "Discard changes").Click();
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        DialogButton(dialog, "Discard changes").Click();

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Published title"));
        var discard = _content.Writes.ShouldHaveSingleItem();
        (discard.Path, discard.IfMatch).ShouldBe(($"content/post/{id:D}/discard", "\"3\""));
        Badges(app).ShouldBe(["en Published"]);
        Controls(app).ShouldBe(["Save", "Unpublish"]);
        Announced(app).ShouldContain("The changes were discarded. The published version is shown.");
        Services.GetRequiredService<IFormDraftStore>().HasUnsavedForms.ShouldBeFalse();

        // The next save is based on the version the discard answered.
        Input(app, "title").Input("After the discard");
        Save(app);
        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        _content.Writes[1].IfMatch.ShouldBe("\"4\"");
    }

    [Fact]
    public void UnpublishAsksFirstAndKeepsTheDraft()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Load($"content/post/{id:D}");

        ToolbarButton(app, "Unpublish").Click();

        var dialog = Dialog(app, "Unpublish this entry?");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        _content.Writes.ShouldBeEmpty();
        dialog.Find("p").TextContent.ShouldStartWith("The entry is taken off the site in en. Its other locales stay as they are");
        dialog.FindAll("fluent-button").Select(button => button.TextContent.Trim()).Where(text => text.Length > 0).ShouldBe(["Keep it published", "Unpublish"]);
        DialogButton(dialog, "Unpublish").Click();

        app.WaitForAssertion(() => Badges(app).ShouldBe(["en Unpublished"]));
        var unpublish = _content.Writes.ShouldHaveSingleItem();
        (unpublish.Path, unpublish.IfMatch, unpublish.Locale).ShouldBe(($"content/post/{id:D}/unpublish", null, null));
        Input(app, "title").GetAttribute("value").ShouldBe("Hello");
        Controls(app).ShouldBe(["Save", "Publish"]);
        Announced(app).ShouldContain("Unpublished.");
    }

    [Fact]
    public void AnUnpublishThatFailsIsShownAndChangesNothing()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Load($"content/post/{id:D}");
        _content.NextWrite = _ => throw new HttpRequestException("The connection closed.");

        ToolbarButton(app, "Unpublish").Click();
        DialogButton(Dialog(app, "Unpublish this entry?"), "Unpublish").Click();

        app.WaitForAssertion(() => app.Find(".content-form-problem").TextContent.ShouldContain("The entry was not unpublished."));
        app.Find(".content-form-problem").TextContent.ShouldContain("The server could not be reached.");
        Badges(app).ShouldBe(["en Published"]);
    }

    [Fact]
    public async Task APublishWaitingOnItsSaveDoesNotPublishTheEntryTheFormMovedTo()
    {
        var first = _content.Add("post", new JsonObject { ["title"] = "First" });
        var second = _content.Add("post", new JsonObject { ["title"] = "Second" });
        var app = Load($"content/post/{first:D}");
        Input(app, "title").Input("Changed");
        var save = new TaskCompletionSource();
        Management.Hold = save;

        // Only the save waits: the form has read the second entry by the time it ends.
        ToolbarButton(app, "Publish").Click();
        app.WaitForAssertion(() => Management.Calls.ShouldContain(call => call.Method == HttpMethod.Put));
        Management.Hold = null;
        Navigation.NavigateTo($"content/post/{second:D}");
        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Second"));
        save.SetResult();

        app.WaitForAssertion(() => _content.Version(first).ShouldBe(2));
        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);
        _content.Writes.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put);
        (_content.Status(first), _content.Status(second)).ShouldBe(("draft", "draft"));
    }

    [Fact]
    public void APublishOnAStaleTagOpensTheConflictAndSaysItDidNotPublish()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        _content.ChangeElsewhere(id, "title", "Theirs");

        ToolbarButton(app, "Publish").Click();

        var dialog = Dialog(app, "Someone saved this entry first");
        app.WaitForAssertion(() => dialog.Instance.Open.ShouldBeTrue());
        _content.Writes.ShouldHaveSingleItem().IfMatch.ShouldBe("\"1\"");
        DialogButton(dialog, "Keep mine").Click();

        app.WaitForAssertion(() => Input(app, "title").GetAttribute("value").ShouldBe("Theirs"));
        const string NotPublished = "The entry was not published, since it was saved after you read it. The saved version is shown now; publish again to publish it.";
        app.Find(".content-form-saved").TextContent.ShouldBe(NotPublished);
        Announced(app).ShouldContain(NotPublished);
        _content.Status(id).ShouldBe("draft");

        ToolbarButton(app, "Publish").Click();
        app.WaitForAssertion(() => _content.Status(id).ShouldBe("published"));
        _content.Writes[^1].IfMatch.ShouldBe("\"2\"");
    }

    [Fact]
    public void ADiscardThatMeetsANewerVersionOffersOnlyAReloadNotAnEarlierSavesConflict()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Draft" }, status: "modified", published: new JsonObject { ["title"] = "Published" });
        var app = Load($"content/post/{id:D}");
        _content.ChangeElsewhere(id, "title", "Theirs");
        Input(app, "title").Input("Mine");
        Save(app);
        var conflict = Dialog(app, "Someone saved this entry first");
        app.WaitForAssertion(() => conflict.Instance.Open.ShouldBeTrue());
        conflict.InvokeAsync(() => conflict.Instance.OpenChanged.InvokeAsync(false));
        app.WaitForAssertion(() => app.FindAll(".content-form-problem fluent-button").Select(button => button.TextContent.Trim()).ShouldContain("Compare the versions"));

        ToolbarButton(app, "Discard changes").Click();
        DialogButton(Dialog(app, "Discard the changes?"), "Discard changes").Click();

        app.WaitForAssertion(() => _content.Writes.Count.ShouldBe(2));
        app.WaitForAssertion(() => app.FindAll(".content-form-problem fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Reload the entry"]));
        Dialog(app, "Someone saved this entry first").Instance.Open.ShouldBeFalse();
    }

    [Fact]
    public async Task TheShortcutPublishesWithoutMovingFocus()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        var focuses = HeadingFocuses();

        await Shortcut(FormShortcut.Publish);

        app.WaitForAssertion(() => _content.Writes.ShouldHaveSingleItem().Path.ShouldBe($"content/post/{id:D}/publish"));
        app.WaitForAssertion(() => Badges(app).ShouldBe(["en Published"]));
        HeadingFocuses().ShouldBe(focuses);
    }

    private async Task Shortcut(FormShortcut shortcut)
    {
        var target = Document.Invocations.Last(call => call.Identifier == "listenForShortcuts").Arguments[0].ShouldBeOfType<DotNetObjectReference<DocumentInterop.ShortcutTarget>>();
        await target.Value.RunAsync(shortcut.ToString().ToLowerInvariant());
    }

    private int HeadingFocuses() => Document.Invocations.Count(call => call.Identifier == "focusHeading");

    private IRenderedComponent<AdminApp> Load(string route)
    {
        Start();
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    private static string[] Controls(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-form-toolbar fluent-button").Select(button => button.TextContent.Trim())];

    private static string[] Badges(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-form-toolbar .content-badge").Select(badge => string.Join(' ', badge.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];

    private static List<string> Announced(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll("[role=status] > div").Select(message => message.TextContent)];

    private static void Save(IRenderedComponent<AdminApp> app) => ToolbarButton(app, "Save").Click();

    private static IElement ToolbarButton(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll(".content-form-toolbar fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IRenderedComponent<CmsDialog> Dialog(IRenderedComponent<AdminApp> app, string title) =>
        app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == title);

    private static IElement DialogButton(IRenderedComponent<CmsDialog> dialog, string text) =>
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    private static IElement Input(IRenderedComponent<AdminApp> app, string field) => app.Find($"[data-field='{field}'] input");

    private static string Message(IRenderedComponent<AdminApp> app, string field)
    {
        var input = Input(app, field);
        return app.Find($"#{input.GetAttribute("aria-describedby")!.Split(' ')[0]}").TextContent;
    }

    // A localized post, so its badge names the locale; author stands for a relation, whose errors a blocking type answers.
    private static JsonObject Post()
    {
        var type = Type("post", fields: [Field("title", required: true), Field("author")]);
        type["localized"] = true;
        return type;
    }
}
