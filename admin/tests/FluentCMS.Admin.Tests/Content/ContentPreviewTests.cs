using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The preview pane of A-29 and its follow-ups (#3403): no button for a type the schema says has no template or without
// preview origins, a token minted only as the pane opens, the draft's page in a frame at phone, tablet or full width, the
// published page while the locale has one, a URL of another origin refused, the page's ready taken only from the frame's
// origin, protocol and version and forgotten when the page navigates or the frame loads another, the hello after each load
// that a page of version 2 answers, a save that tells a ready page to refresh in its version and reloads a silent one, a token minted again once it ended, and the newest mint's answer used.
public sealed class ContentPreviewTests : AdminTestContext
{
    private const string FrontEnd = "https://www.example.com";
    private const string Previewing = """{ "apiBaseUrl": "https://cms.example.com", "previewOrigins": ["https://www.example.com"] }""";
    private const string Silent = "This page does not answer the preview, so each save loads it again.";
    private const string NotRefreshed = "The preview could not be refreshed; Reload tries again.";

    private readonly FakeContent _content;
    private readonly BunitJSInterop _preview;
    private readonly BunitJSInterop _listener;

    // The clock of the pane: a page's five seconds to say ready pass only when a test moves it.
    private readonly MovableTime _time = new();

    public ContentPreviewTests()
    {
        UseNeutralResources();
        Services.AddSingleton<TimeProvider>(_time);
        _content = new FakeContent(Management);
        // The server's clock is the browser's and its answers carry a Date, as for an admin on the API's origin.
        _content.PreviewClock = _time.Now;
        Management.SetTypes(MainSite, Type("post", fields: [Field("title")]));
        _preview = JSInterop.SetupModule(PreviewInterop.ModulePath);
        _listener = _preview.SetupModule(invocation => invocation.Identifier == "listen");
        _preview.SetupVoid("post", _ => true).SetVoidResult();
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", null);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void ATypeTheSchemaSaysHasNoTemplateShowsNoButtonAndMintsNothing()
    {
        var post = Type("post", fields: [Field("title")]);
        post["previewable"] = false;
        Management.SetTypes(MainSite, post);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });

        var app = Load($"content/post/{id:D}");

        Buttons(app).ShouldBe(["Version history"]);
        _content.PreviewMints.ShouldBe(0);
    }

    // #3432 adds the member; a server without it, and one that says the type has a template, both get the button, and the
    // token is minted only as the pane opens, not on the form's read, save or publish.
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void ATokenIsMintedOnlyAsThePaneOpens(bool? previewable)
    {
        var post = Type("post", fields: [Field("title")]);
        if (previewable is { } value)
        {
            post["previewable"] = value;
        }

        Management.SetTypes(MainSite, post);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        Buttons(app).ShouldBe(["Version history", "Preview"]);

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);
        app.WaitForAssertion(() => app.Find(".content-form-saved").TextContent.ShouldNotBeEmpty());
        ToolbarButton(app, "Publish").Click();
        app.WaitForAssertion(() => _content.Status(id).ShouldBe("published"));
        _content.PreviewMints.ShouldBe(0);

        Open(app);

        _content.PreviewMints.ShouldBe(1);
    }

    [Fact]
    public void APaneOfATypeWithoutATemplateSaysSo()
    {
        _content.Preview = (_, _) => (null, null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        Button(app, "Preview").Click();

        app.WaitForAssertion(() => Notices(app).ShouldBe(["The site has no preview page for this type."]));
        app.FindAll(".content-preview iframe").ShouldBeEmpty();
        SaveChanged(app);
        _content.PreviewMints.ShouldBe(1);
    }

    [Fact]
    public void WithoutPreviewOriginsNoTokenIsMinted()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });

        var app = Load($"content/post/{id:D}", AdminTestContext.ValidConfiguration);

        Buttons(app).ShouldBe(["Version history"]);
        _content.PreviewMints.ShouldBe(0);
    }

    [Fact]
    public void TheDraftIsShownAtThreeWidthsAndThePublishedPageWhileThereIsOne()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", $"{FrontEnd}/posts/{id:D}");
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Open(Load($"content/post/{id:D}"));

        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
        Frame(app).GetAttribute("sandbox").ShouldBe("allow-scripts allow-same-origin allow-forms allow-popups");
        _content.PreviewMints.ShouldBe(1);
        Pane(app).GetAttribute("data-width").ShouldBe("phone");
        app.Find(".content-form-layout").GetAttribute("data-preview").ShouldBe("beside");

        var width = app.FindComponents<CmsSelect>().Single(select => select.Instance.Label == "Width");
        app.InvokeAsync(() => width.Instance.ValueChanged.InvokeAsync(ContentPreview.Tablet));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-width").ShouldBe("tablet"));
        app.InvokeAsync(() => width.Instance.ValueChanged.InvokeAsync(ContentPreview.Full));
        app.WaitForAssertion(() => app.Find(".content-form-layout").GetAttribute("data-preview").ShouldBe("full"));

        app.InvokeAsync(() => Published(app).Instance.ValueChanged.InvokeAsync(true));
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}"));
    }

    [Fact]
    public void TheToggleIsOffForALocaleNeverPublished()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });

        var app = Open(Load($"content/post/{id:D}"));

        Published(app).Instance.Disabled.ShouldBeTrue();
    }

    [Fact]
    public void APageOfAnotherOriginIsRefused()
    {
        _content.Preview = (id, _) => ($"https://elsewhere.example.com/posts/{id:D}?preview={{token}}", null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        Button(app, "Preview").Click();

        app.WaitForAssertion(() => Notices(app).ShouldBe(["The preview page is not on an origin the admin may show."]));
        app.FindAll(".content-preview iframe").ShouldBeEmpty();
        SaveChanged(app);
        _content.PreviewMints.ShouldBe(1);
    }

    [Fact]
    public void AReadyPageIsToldToRefreshAfterASaveAndOtherMessagesAreIgnored()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Post(app, "https://elsewhere.example.com", Message("ready"));
        Post(app, FrontEnd, Message("ready", protocol: "other"));
        Post(app, FrontEnd, Message("ready", version: 3));
        Post(app, FrontEnd, Message("hello"));
        Post(app, FrontEnd, "\"ready\"");
        Pane(app).HasAttribute("data-ready").ShouldBeFalse();

        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));
        var loaded = Frame(app).GetAttribute("src");

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);

        app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
        var sent = _preview.Invocations.Single(call => call.Identifier == "post");
        sent.Arguments[1].ShouldBe(FrontEnd);
        var refresh = JsonNode.Parse((string)sent.Arguments[2]!)!.AsObject();
        refresh.ToJsonString().ShouldBe(new JsonObject
        {
            ["protocol"] = "fluentcms-preview",
            ["version"] = 1,
            ["type"] = "refresh",
            ["entry"] = new JsonObject { ["type"] = "post", ["id"] = id.ToString("D"), ["locale"] = null, ["version"] = 2 },
        }.ToJsonString());
        Frame(app).GetAttribute("src").ShouldBe(loaded);
        _content.PreviewMints.ShouldBe(1);

        // The page that said ready is leaving, so the next save loads the frame unless the next page says it again.
        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/about"));
        app.WaitForAssertion(() => Notices(app).ShouldBe([$"Showing {FrontEnd}/about"]));
        Pane(app).HasAttribute("data-ready").ShouldBeFalse();
    }

    // For a page of version 1, which ignores hello, a ready that comes before the load of the page that sent it is kept; a
    // load with none since the one before is of another page, which gets the five seconds again and is loaded again by a save.
    [Fact]
    public void ReadyIsForgottenWhenTheFrameLoadsAPageThatDoesNotSayIt()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        Loaded(app);
        Pane(app).GetAttribute("data-ready").ShouldBe("true");

        Loaded(app);
        app.WaitForAssertion(() => Pane(app).HasAttribute("data-ready").ShouldBeFalse());
        _time.Advance(TimeSpan.FromSeconds(5));
        app.WaitForAssertion(() => Notices(app).ShouldBe([Silent]));
        SaveChanged(app);

        app.WaitForAssertion(() => Notices(app).ShouldBeEmpty());
        Posted("refresh").ShouldBeEmpty();
    }

    // After each load the pane says hello to the frame's origin, as version 2 and with nothing else.
    [Fact]
    public void EachLoadIsFollowedByAHello()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Loaded(app);
        Loaded(app);

        var hellos = _preview.Invocations.Where(call => call.Identifier == "post").ToList();
        hellos.Count.ShouldBe(2);
        hellos.ShouldAllBe(call => (string)call.Arguments[1]! == FrontEnd);
        var hello = new JsonObject { ["protocol"] = "fluentcms-preview", ["version"] = 2, ["type"] = "hello" }.ToJsonString();
        hellos.ShouldAllBe(call => (string)call.Arguments[2]! == hello);
    }

    // A load ends the ready of a page of version 2 whichever of the two came first, and the page's answer to the hello sets it
    // again; a save then tells the page to refresh in version 2. A load of a page that answers no hello, such as one the page
    // navigated to without saying so, leaves the pane waiting even though a ready came since the load before, and a save
    // loads the frame again.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReadyOfVersion2IsOfTheDocumentThatSaidIt(bool readyFirst)
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        if (readyFirst)
        {
            Post(app, FrontEnd, Message("ready", version: 2));
            Pane(app).GetAttribute("data-ready").ShouldBe("true");
        }

        Loaded(app);
        app.WaitForAssertion(() => Pane(app).HasAttribute("data-ready").ShouldBeFalse());
        Posted("hello").Count.ShouldBe(1);
        Post(app, FrontEnd, Message("ready", version: 2));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));

        SaveChanged(app);
        app.WaitForAssertion(() => Posted("refresh").Count.ShouldBe(1));
        Posted("refresh").Single()["version"]!.GetValue<int>().ShouldBe(2);

        Loaded(app);
        app.WaitForAssertion(() => Pane(app).HasAttribute("data-ready").ShouldBeFalse());
        Posted("hello").Count.ShouldBe(2);
        _time.Advance(TimeSpan.FromSeconds(5));
        app.WaitForAssertion(() => Notices(app).ShouldBe([Silent]));
        SaveChanged(app);

        app.WaitForAssertion(() => Notices(app).ShouldBeEmpty());
        Posted("refresh").Count.ShouldBe(1);
    }

    // A page of version 1 keeps what it had: where the frame's load comes before its ready, a load of a page it navigated to
    // without saying so is taken for its own, and a save tells it to refresh in version 1 (spec 4.12).
    [Fact]
    public void APageOfVersion1WhoseLoadComesFirstKeepsReadyThroughALoadItDidNotAnnounce()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Loaded(app);
        Post(app, FrontEnd, Message("ready"));

        Loaded(app);
        Pane(app).GetAttribute("data-ready").ShouldBe("true");
        SaveChanged(app);

        app.WaitForAssertion(() => Posted("refresh").Count.ShouldBe(1));
        Posted("refresh").Single()["version"]!.GetValue<int>().ShouldBe(1);
    }

    // The load starts the five seconds again, so the note of a silent page goes; the token still lasts, so none is minted.
    [Fact]
    public void ASaveReloadsAPageThatSaidNothingWithTheTokenItHas()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        _time.Advance(TimeSpan.FromSeconds(5));
        app.WaitForAssertion(() => Notices(app).ShouldBe([Silent]));

        SaveChanged(app);

        app.WaitForAssertion(() => Notices(app).ShouldBeEmpty());
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
        _content.PreviewMints.ShouldBe(1);
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // A ready page is told to refresh only while its token lasts; an ending one is minted again and the page loaded with it.
    [Fact]
    public void ASaveLoadsAReadyPageWhoseTokenIsEndingWithANewOne()
    {
        _content.PreviewLifetime = TimeSpan.FromSeconds(30);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // An ended token, and one less than 45 seconds from its end, is minted again rather than loaded.
    [Theory]
    [InlineData(-60)]
    [InlineData(30)]
    public void AnEndingTokenIsMintedAgainWhenTheFrameReloads(int lifetime)
    {
        _content.PreviewLifetime = TimeSpan.FromSeconds(lifetime);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");

        Button(app, "Reload").Click();

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
    }

    [Fact]
    public void ALastingTokenIsLoadedAgainWithoutAMint()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));

        Button(app, "Reload").Click();

        app.WaitForAssertion(() => Pane(app).HasAttribute("data-ready").ShouldBeFalse());
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
        _content.PreviewMints.ShouldBe(1);
    }

    [Fact]
    public void APublishWithThePaneClosedOffersThePublishedPageWhenItOpens()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", _content.Status(id) == "published" ? $"{FrontEnd}/posts/{id:D}" : null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");

        ToolbarButton(app, "Publish").Click();
        app.WaitForAssertion(() => _content.Status(id).ShouldBe("published"));
        Open(app);

        Published(app).Instance.Disabled.ShouldBeFalse();
        _content.PreviewMints.ShouldBe(1);
    }

    [Fact]
    public void APublishWithThePaneOpenOffersThePublishedPage()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", _content.Status(id) == "published" ? $"{FrontEnd}/posts/{id:D}" : null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Published(app).Instance.Disabled.ShouldBeTrue();
        Post(app, FrontEnd, Message("ready"));

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => Published(app).Instance.Disabled.ShouldBeFalse());
        _content.PreviewMints.ShouldBe(2);
        _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1);
    }

    // An unpublish while the pane shows the published page leaves no published page: the draft is loaded, with a new token,
    // and the page that said ready is not told to refresh.
    [Fact]
    public void AnUnpublishWhileThePublishedPageIsShownLoadsTheDraft()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", _content.Status(id) == "published" ? $"{FrontEnd}/posts/{id:D}" : null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Open(Load($"content/post/{id:D}"));
        app.InvokeAsync(() => Published(app).Instance.ValueChanged.InvokeAsync(true));
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));

        ToolbarButton(app, "Unpublish").Click();
        var dialog = app.FindComponents<CmsDialog>().Single(found => found.Instance.Title == "Unpublish this entry?");
        dialog.FindAll("fluent-button").Single(button => button.TextContent.Trim() == "Unpublish").Click();

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        Published(app).Instance.Value.ShouldBeFalse();
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // The server checks a token's end by its own clock. With the browser's five minutes behind, the token is ending after
    // nine and a half minutes by the browser's clock too, so a save mints again instead of refreshing a page whose token the
    // server would refuse.
    [Fact]
    public void ATokenEndsByTheServersClockWhenTheBrowsersIsBehind()
    {
        var server = _time.Now;
        (_content.PreviewClock, _time.Now) = (server, server - TimeSpan.FromMinutes(5));
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        _time.Advance(TimeSpan.FromSeconds(570));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // With the browser's clock nine and a half minutes ahead, a fresh token is not taken for an ending one, so a save
    // refreshes the page with no mint.
    [Fact]
    public void ATokenEndsByTheServersClockWhenTheBrowsersIsAhead()
    {
        var server = _time.Now;
        (_content.PreviewClock, _time.Now) = (server, server + TimeSpan.FromSeconds(570));
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));

        SaveChanged(app);

        app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
        _content.PreviewMints.ShouldBe(1);
    }

    // An admin on another origin sees no Date, and the answer gives no lifetime yet (#3473): only the minute the server issues
    // at least is trusted, so within fifteen seconds of a mint a save refreshes a ready page, and after them every save and
    // every Reload mints, and a ready page is loaded with the new token rather than told to refresh.
    [Fact]
    public void WithNoDateAndNoLifetimeOnlyTheShortestLifetimeIsTrusted()
    {
        _content.PreviewClock = null;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        _time.Advance(TimeSpan.FromSeconds(10));
        SaveChanged(app);
        app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
        _content.PreviewMints.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(6));
        SaveChanged(app);
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _time.Advance(TimeSpan.FromSeconds(16));
        Button(app, "Reload").Click();

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-3"));
        _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1);
    }

    // The lifetime the answer gives counts from when the mint was sent, with no Date and the browser's clock behind.
    [Fact]
    public void TheLifetimeTheAnswerGivesIsAnEndWithoutADate()
    {
        (_content.PreviewClock, _content.PreviewExpiresIn) = (null, 600);
        _time.Now -= TimeSpan.FromMinutes(5);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));

        SaveChanged(app);

        app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
        _content.PreviewMints.ShouldBe(1);
    }

    // The lifetime the answer gives wins over the Date: here the Date says ten minutes are left and the lifetime one.
    [Fact]
    public void TheLifetimeTheAnswerGivesWinsOverTheDate()
    {
        _content.PreviewExpiresIn = 60;
        _time.Now -= TimeSpan.FromMinutes(5);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        _time.Advance(TimeSpan.FromSeconds(20));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // A lifetime with a fraction reads, and only its whole seconds count: 60.9 seconds end, less the 45 of the margin, after 15.
    [Fact]
    public void ALifetimeWithAFractionCountsItsWholeSeconds()
    {
        _content.PreviewExpiresIn = 60.9;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        _time.Advance(TimeSpan.FromSeconds(15.5));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // The end is on the monotonic clock: the computer's clock set back five minutes while the pane is open does not give the
    // token more time, so nine and a half minutes after its mint a save mints again.
    [Fact]
    public void AClockSetBackDoesNotMoveTheEnd()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        _time.Now -= TimeSpan.FromMinutes(5);
        _time.Advance(TimeSpan.FromSeconds(570));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // A Reload of a ready draft whose token ended mints, and that fails: the pane says so and keeps nothing of the page it
    // showed, ready included.
    [Fact]
    public void AFailedMintOfAReloadKeepsNothingOfThePage()
    {
        _content.PreviewLifetime = TimeSpan.FromMinutes(-1);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));
        _content.PreviewUnavailable = true;

        Button(app, "Reload").Click();

        app.WaitForAssertion(() => app.FindAll(".content-preview > .content-preview-notice").Select(notice => notice.TextContent.Trim()).ShouldBe(["The preview could not be loaded."]));
        Pane(app).HasAttribute("data-ready").ShouldBeFalse();
        app.FindAll(".content-preview iframe").ShouldBeEmpty();
        var pane = app.FindComponent<ContentPreview>().Instance;
        Member(pane, "frameToken").ShouldBeNull();
        Member(pane, "frameDeadline").ShouldBeNull();
        Member(pane, "frameDraft").ShouldBe(false);
    }

    // A lifetime that is not a number, endless, negative, nothing or past what a TimeSpan holds does not break the pane: what
    // is not a number or below nothing is none, and the rest is held to whole seconds up to int.MaxValue. A save of a ready
    // page then mints for an ended token and refreshes for a lasting one.
    [Theory]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, true)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(-10d, false)]
    [InlineData(0d, false)]
    [InlineData(1e300, true)]
    public void AnyLifetimeIsHeldToWholeSecondsItCanCount(double seconds, bool lasts)
    {
        _content.PreviewExpiresIn = seconds;
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
        Post(app, FrontEnd, Message("ready"));

        SaveChanged(app);

        if (lasts)
        {
            app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
            _content.PreviewMints.ShouldBe(1);
        }
        else
        {
            app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
            _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
        }
    }

    // A sleep may stop the monotonic clock while the computer's goes on: the computer's clock past the lifetime with no
    // monotonic time passed ends the token, so a save mints.
    [Fact]
    public void ASleepPastTheLifetimeEndsTheToken()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        _time.Now += TimeSpan.FromMinutes(10);

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // A Date later than the token's end leaves no time: the end has passed, so a save mints and loads the page with the new token.
    [Fact]
    public void ADateLaterThanTheEndIsAnEndPassed()
    {
        _content.PreviewLifetime = TimeSpan.FromMinutes(-1);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // The two minutes the mint took count against the token: ten and a half minutes after it was sent, eight and a half after
    // its answer, the token is ending, so a save mints.
    [Fact]
    public async Task TheEndCountsFromWhenTheMintWasSent()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        var held = new TaskCompletionSource();
        var mints = 0;
        Management.HoldPath = path => path.Contains("/preview-token", StringComparison.Ordinal) && ++mints == 1 ? held.Task : null;
        Button(app, "Preview").Click();
        app.WaitForAssertion(() => mints.ShouldBe(1));
        _time.Advance(TimeSpan.FromMinutes(2));
        await Release(app, held);
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1"));
        Post(app, FrontEnd, Message("ready"));
        _time.Advance(TimeSpan.FromSeconds(510));

        SaveChanged(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    // The toggle keeps its name and says by aria-expanded whether the pane is open; the pane's own Close has a name of its
    // own. Opening moves focus to the pane, which comes after the form in the page as on the screen, and closing the pane by
    // its Close puts it back on the toggle.
    [Fact]
    public void ThePaneTakesFocusAsItOpensAndGivesItBackToTheToggleAsItCloses()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        var toggle = Button(app, "Preview");
        toggle.GetAttribute("aria-expanded").ShouldBe("true");
        toggle.GetAttribute("aria-controls").ShouldBe(Pane(app).Id);
        app.FindAll("fluent-button").Count(button => button.TextContent.Trim() == "Close preview").ShouldBe(1);
        // The pane is the element the frame's messages are listened for on.
        var pane = ((ElementReference)_preview.Invocations.Last(call => call.Identifier == "listen").Arguments[0]!).Id;
        app.WaitForAssertion(() => Focused().ShouldBe(pane));
        Pane(app).GetAttribute("tabindex").ShouldBe("-1");
        app.Find(".content-form-layout").Children.Select(child => child.ClassName).ShouldBe(["content-form", "content-preview"]);

        app.FindAll(".content-preview fluent-button").Single(button => button.TextContent.Trim() == "Close preview").Click();

        app.WaitForAssertion(() => app.FindAll(".content-preview").ShouldBeEmpty());
        var opener = Button(app, "Preview");
        opener.GetAttribute("aria-expanded").ShouldBe("false");
        opener.HasAttribute("aria-controls").ShouldBeFalse();
        var button = app.FindComponents<Microsoft.FluentUI.AspNetCore.Components.FluentButton>().Single(found => found.Nodes.OfType<IElement>().First().TextContent.Trim() == "Preview");
        app.WaitForAssertion(() => Focused().ShouldBe(button.Instance.Element.Id));
    }

    // The notices other than a failure, which is announced, are in a live region that is there before them.
    [Fact]
    public void TheNoticesAreInALiveRegion()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        var region = app.Find(".content-preview-notices");
        region.GetAttribute("role").ShouldBe("status");

        _time.Advance(TimeSpan.FromSeconds(5));

        app.WaitForAssertion(() => app.Find(".content-preview-notices").TextContent.Trim().ShouldBe(Silent));
    }

    // A frame of the admin's own origin with scripts and the same origin allowed would reach its sessionStorage.
    [Fact]
    public void APageOfTheAdminsOwnOriginIsRefused()
    {
        _content.Preview = (id, _) => ($"http://localhost/posts/{id:D}?preview={{token}}", null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}", """{ "apiBaseUrl": "https://cms.example.com", "previewOrigins": ["http://localhost"] }""");

        Button(app, "Preview").Click();

        app.WaitForAssertion(() => Notices(app).ShouldBe(["The preview page is not on an origin the admin may show."]));
        app.FindAll(".content-preview iframe").ShouldBeEmpty();
    }

    // config.json and the server's templates take ASCII alone, so an internationalized host comes in punycode, as the browser
    // writes the origin of a message; an address in Unicode, which this server does not hand out, is compared in that form too.
    [Fact]
    public void AnInternationalizedHostIsMatchedInItsAsciiForm()
    {
        _content.Preview = (id, _) => ($"https://bücher.example/posts/{id:D}?preview={{token}}", null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}", """{ "apiBaseUrl": "https://cms.example.com", "previewOrigins": ["https://xn--bcher-kva.example"] }"""));

        Post(app, "https://xn--bcher-kva.example", Message("ready"));

        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));
        Post(app, "https://xn--bcher-kva.example", Message("navigated", url: "https://xn--bcher-kva.example/about"));
        app.WaitForAssertion(() => Notices(app).ShouldBe(["Showing https://xn--bcher-kva.example/about"]));
    }

    [Fact]
    public void AFailedMintAfterAPublishLoadsASilentPageAgainWithTheTokenItHas()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        _time.Advance(TimeSpan.FromSeconds(5));
        app.WaitForAssertion(() => Notices(app).ShouldBe([Silent]));
        _content.PreviewUnavailable = true;

        // A publish may bring the published page, so it mints.
        ToolbarButton(app, "Publish").Click();

        // The load starts the five seconds again, so the note of a silent page is gone until they pass.
        app.WaitForAssertion(() => Notices(app).ShouldBeEmpty());
        _content.PreviewMints.ShouldBe(2);
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
        Assertive(app).ShouldBeEmpty();
    }

    [Fact]
    public void AFailedMintAfterASaveKeepsAPageWhoseTokenEndedWithANote()
    {
        _content.PreviewLifetime = TimeSpan.FromMinutes(-1);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        _content.PreviewUnavailable = true;

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);

        app.WaitForAssertion(() => app.FindAll(".content-preview > .content-preview-notice").Select(notice => notice.TextContent.Trim()).ShouldBe([NotRefreshed]));
        app.WaitForAssertion(() => Assertive(app).ShouldBe([NotRefreshed]));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
    }

    // The page is refreshed, so nothing failed that the editor sees; the answer is old, so Reload mints again, and keeps the
    // page while its token lasts when that mint fails too.
    [Fact]
    public void AFailedMintAfterAPublishStillRefreshesAReadyPageWithoutANote()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));
        _content.PreviewUnavailable = true;

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
        _content.PreviewMints.ShouldBe(2);
        app.FindAll(".content-preview > .content-preview-notice").ShouldBeEmpty();
        Assertive(app).ShouldBeEmpty();

        Button(app, "Reload").Click();
        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(3));
        app.WaitForAssertion(() => Pane(app).HasAttribute("data-ready").ShouldBeFalse());
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
        app.FindAll(".content-preview > .content-preview-notice").ShouldBeEmpty();

        _content.PreviewUnavailable = false;
        Button(app, "Reload").Click();
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-4"));
    }

    // The published switch mints after a failed mint, as Reload does, and keeps the frame when that mint fails too, showing the
    // published page of the answer it has.
    [Fact]
    public void AFailedMintKeepsTheFrameOnASwitchToThePublishedPage()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", $"{FrontEnd}/posts/{id:D}");
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" }, status: "published");
        var app = Open(Load($"content/post/{id:D}"));
        _content.PreviewUnavailable = true;

        // The save makes the published locale modified: a change of status, so it mints, and that fails.
        SaveChanged(app);
        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(2));
        app.InvokeAsync(() => Published(app).Instance.ValueChanged.InvokeAsync(true));

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}"));
        _content.PreviewMints.ShouldBe(3);
        app.FindAll(".content-preview > .content-preview-notice").ShouldBeEmpty();
        Assertive(app).ShouldBeEmpty();
    }

    // The mint of a save is answered after the mint of a later Reload; the Reload's answer stays.
    [Fact]
    public async Task TheNewestMintWins()
    {
        _content.PreviewLifetime = TimeSpan.FromMinutes(-1);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        var held = new TaskCompletionSource();
        var mints = 0;
        Management.HoldPath = path => path.Contains("/preview-token", StringComparison.Ordinal) && ++mints == 1 ? held.Task : null;

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);
        app.WaitForAssertion(() => mints.ShouldBe(1));
        Button(app, "Reload").Click();
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));

        await Release(app, held);

        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(3));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2");
    }

    // The pane opens and a save follows while its mint runs; the open's answer is dropped and the save's mint fails, so the
    // pane says it failed instead of loading for good.
    [Fact]
    public async Task ANewerMintThatFailsIsShownAfterAnOlderAnswerIsDropped()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        var (first, second) = (new TaskCompletionSource(), new TaskCompletionSource());
        var mints = 0;
        Management.HoldPath = path => path.Contains("/preview-token", StringComparison.Ordinal) ? (++mints == 1 ? first.Task : second.Task) : null;
        Button(app, "Preview").Click();
        app.WaitForAssertion(() => mints.ShouldBe(1));

        SaveChanged(app);
        app.WaitForAssertion(() => mints.ShouldBe(2));
        await Release(app, first);
        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(1));
        _content.PreviewUnavailable = true;
        await Release(app, second);

        app.WaitForAssertion(() => app.FindAll(".content-preview > .content-preview-notice").Select(notice => notice.TextContent.Trim()).ShouldBe(["The preview could not be loaded."]));
        app.WaitForAssertion(() => Assertive(app).ShouldBe(["The preview could not be loaded."]));
        app.FindAll(".content-preview iframe").ShouldBeEmpty();
        Notices(app).ShouldBeEmpty();
    }

    [Fact]
    public async Task ClosingThePaneWhileItsMintRunsDropsTheAnswer()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Load($"content/post/{id:D}");
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.Contains("/preview-token", StringComparison.Ordinal) ? held.Task : null;
        _content.PreviewUnavailable = true;
        Button(app, "Preview").Click();
        app.WaitForAssertion(() => _preview.Invocations.ShouldContain(call => call.Identifier == "listen"));

        Button(app, "Preview").Click();
        app.WaitForAssertion(() => app.FindAll(".content-preview").ShouldBeEmpty());
        await Release(app, held);

        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(1));
        Assertive(app).ShouldBeEmpty();
        _listener.Invocations.ShouldContain(call => call.Identifier == "dispose");
    }

    // A switch of locale keeps the pane, with a token of the locale; another entry closes it.
    [Fact]
    public void AnotherLocaleKeepsThePaneAndAnotherEntryClosesIt()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var post = Type("post", fields: [Field("title")]);
        post["localized"] = true;
        Management.SetTypes(MainSite, post);
        _content.Preview = (id, locale) => ($"{FrontEnd}/{locale}/posts/{id:D}?preview={{token}}", null);
        var first = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var second = _content.Add("post", new JsonObject { ["title"] = "World" });
        _content.Add("post", new JsonObject { ["title"] = "Monde" }, id: second, locale: "fr");
        var app = Open(Load($"content/post/{second:D}"));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/en/posts/{second:D}?preview=token-1");

        Navigation.NavigateTo($"content/post/{second:D}?locale=fr");
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/fr/posts/{second:D}?preview=token-2"));

        Navigation.NavigateTo($"content/post/{first:D}?locale=en");
        app.WaitForAssertion(() => app.Find("[data-field='title'] input").GetAttribute("value").ShouldBe("Hello"));
        app.FindAll(".content-preview").ShouldBeEmpty();
        Button(app, "Preview").GetAttribute("aria-expanded").ShouldBe("false");
        _content.PreviewMints.ShouldBe(2);
    }

    // The page reuses the form for a new entry: its first save moves to the entry's route without opening the pane.
    [Fact]
    public void ANewEntryAfterAnOpenPaneDoesNotOpenIt()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Navigation.NavigateTo("content/post/new");
        app.WaitForAssertion(() => app.FindAll(".content-preview").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("[data-field='title'] input").GetAttribute("value").ShouldBeNull());
        app.Find("[data-field='title'] input").Input("New");
        Save(app);

        app.WaitForAssertion(() => Navigation.Uri.ShouldNotEndWith("/new"));
        app.WaitForAssertion(() => Buttons(app).ShouldContain("Preview"));
        app.FindAll(".content-preview").ShouldBeEmpty();
        _content.PreviewMints.ShouldBe(1);
    }

    [Fact]
    public void ANavigatedAddressIsShownOnTheFramesOriginOnlyAndWithoutItsQuery()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));

        Post(app, FrontEnd, Message("navigated", url: "https://elsewhere.example.com/about"));
        Post(app, FrontEnd, Message("navigated", url: "javascript:alert(1)"));
        Notices(app).ShouldBeEmpty();

        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/about?preview=secret#top"));

        app.WaitForAssertion(() => Notices(app).ShouldBe([$"Showing {FrontEnd}/about"]));
    }

    // Uri takes a host with a joiner, which IDN refuses: it has no origin, so its address is not shown.
    [Fact]
    public void AnAddressWhoseHostIdnRefusesIsNotShown()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Post(app, FrontEnd, Message("navigated", url: "https://a\u200Db.example/about"));
        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/about"));

        app.WaitForAssertion(() => Notices(app).ShouldBe([$"Showing {FrontEnd}/about"]));
    }

    [Fact]
    public void ATokenATemplatePutsInThePathIsNotShown()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/preview/{{token}}/posts/{id:D}", null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/preview/token-1/about"));

        app.WaitForAssertion(() => Notices(app).ShouldBe([$"Showing {FrontEnd}/preview/…/about"]));
    }

    // Another entry on the same pane drops the page shown at once, before its own mint answers.
    [Fact]
    public async Task AnotherEntryDropsThePageShownAtOnce()
    {
        Start(Previewing);
        var (first, second) = (_content.Add("post", new JsonObject { ["title"] = "Hello" }), _content.Add("post", new JsonObject { ["title"] = "World" }));
        var pane = Render<ContentPreview>(parameters => parameters.Add(p => p.Type, "post").Add(p => p.EntryId, first).Add(p => p.Version, 1));
        pane.WaitForAssertion(() => pane.Find("iframe").GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{first:D}?preview=token-1"));
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.Contains("/preview-token", StringComparison.Ordinal) ? held.Task : null;

        pane.Render(parameters => parameters.Add(p => p.EntryId, second));

        pane.WaitForAssertion(() => pane.FindAll("iframe").ShouldBeEmpty());
        pane.Find(".content-preview-notices").TextContent.Trim().ShouldBe("Loading the preview\u2026");
        await pane.InvokeAsync(held.SetResult);
        pane.WaitForAssertion(() => pane.Find("iframe").GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{second:D}?preview=token-2"));
    }

    // Another entry on the same pane drops the notice of the earlier one at once, before its own mint answers.
    [Theory]
    [InlineData("missing", "The site has no preview page for this type.")]
    [InlineData("refused", "The preview page is not on an origin the admin may show.")]
    [InlineData("failed", "The preview could not be loaded.")]
    public async Task AnotherEntryDropsTheNoticeShownAtOnce(string state, string notice)
    {
        Start(Previewing);
        var (first, second) = (_content.Add("post", new JsonObject { ["title"] = "Hello" }), _content.Add("post", new JsonObject { ["title"] = "World" }));
        _content.Preview = (id, _) => id != first ? ($"{FrontEnd}/posts/{id:D}?preview={{token}}", null)
            : state == "missing" ? (null, null)
            : ($"https://elsewhere.example.com/posts/{id:D}?preview={{token}}", null);
        _content.PreviewUnavailable = state == "failed";
        var pane = Render<ContentPreview>(parameters => parameters.Add(p => p.Type, "post").Add(p => p.EntryId, first).Add(p => p.Version, 1));
        pane.WaitForAssertion(() => pane.Find(".content-preview-notice").TextContent.Trim().ShouldBe(notice));
        _content.PreviewUnavailable = false;
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.Contains("/preview-token", StringComparison.Ordinal) ? held.Task : null;

        pane.Render(parameters => parameters.Add(p => p.EntryId, second));

        pane.WaitForAssertion(() => pane.FindAll(".content-preview-notice").ShouldBeEmpty());
        pane.Find(".content-preview-notices").TextContent.Trim().ShouldBe("Loading the preview\u2026");
        await pane.InvokeAsync(held.SetResult);
        pane.WaitForAssertion(() => pane.Find("iframe").GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{second:D}?preview=token-2"));
    }

    // The form keeps a reference to a closed pane, which holds no token or address of it then.
    [Fact]
    public async Task AClosedPaneLetsGoOfItsTokenAndAddress()
    {
        Start(Previewing);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var pane = Render<ContentPreview>(parameters => parameters.Add(p => p.Type, "post").Add(p => p.EntryId, id).Add(p => p.Version, 1));
        pane.WaitForAssertion(() => pane.Find("iframe"));
        var instance = pane.Instance;

        await DisposeComponentsAsync();

        foreach (var name in (string[])["answer", "answerDeadline", "frameUrl", "frameToken", "frameDeadline"])
        {
            Member(instance, name).ShouldBeNull(name);
        }
    }

    // Each load starts the five seconds again, and the wait of an earlier load counts for nothing.
    [Fact]
    public async Task APageThatSaysNoReadyIsShownWithANote()
    {
        Start(Previewing);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var pane = Render<ContentPreview>(parameters => parameters
            .Add(p => p.Type, "post")
            .Add(p => p.EntryId, id)
            .Add(p => p.Version, 1));
        pane.WaitForAssertion(() => pane.Find("iframe").GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1"));

        _time.Advance(TimeSpan.FromSeconds(3));
        pane.FindAll("button, fluent-button").First(button => button.TextContent.Trim() == "Reload").Click();
        _time.Advance(TimeSpan.FromSeconds(3));
        await pane.InvokeAsync(() => { });
        pane.FindAll(".content-preview-notice").ShouldBeEmpty();

        _time.Advance(TimeSpan.FromSeconds(2));

        pane.WaitForAssertion(() => pane.Find(".content-preview-notice").TextContent.Trim().ShouldBe(Silent));
        _content.PreviewMints.ShouldBe(1);
    }

    private IRenderedComponent<AdminApp> Load(string route, string configuration = Previewing)
    {
        Start(configuration);
        Navigation.NavigateTo(route);
        var app = Render<AdminApp>();
        app.WaitForAssertion(() => app.FindAll(".content-form p[role='status']").ShouldBeEmpty());
        app.WaitForAssertion(() => app.Find("main h1"));
        return app;
    }

    private static IRenderedComponent<AdminApp> Open(IRenderedComponent<AdminApp> app)
    {
        app.WaitForAssertion(() => Buttons(app).ShouldContain("Preview"));
        Button(app, "Preview").Click();
        app.WaitForAssertion(() => Frame(app));
        return app;
    }

    private PreviewInterop.MessageTarget Target() =>
        _preview.Invocations.Last(call => call.Identifier == "listen").Arguments[1].ShouldBeOfType<Microsoft.JSInterop.DotNetObjectReference<PreviewInterop.MessageTarget>>().Value;

    private void Post(IRenderedComponent<AdminApp> app, string origin, string message) =>
        app.InvokeAsync(() => Target().ReceiveAsync(origin, message)).GetAwaiter().GetResult();

    private void Loaded(IRenderedComponent<AdminApp> app) => app.InvokeAsync(() => Target().LoadedAsync()).GetAwaiter().GetResult();

    // The messages of a type the pane posted to the frame.
    private List<JsonObject> Posted(string type) =>
        [.. _preview.Invocations.Where(call => call.Identifier == "post")
            .Select(call => JsonNode.Parse((string)call.Arguments[2]!)!.AsObject())
            .Where(message => (string?)message["type"] == type)];

    // The id of the element last focused.
    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => ((ElementReference)call.Arguments[0]!).Id).LastOrDefault();

    private static object? Member(ContentPreview pane, string name) =>
        typeof(ContentPreview).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pane);

    // A held answer is let go on the renderer, so that what it is handed on to runs there in turn.
    private static Task Release(IRenderedComponent<AdminApp> app, TaskCompletionSource held) => app.InvokeAsync(held.SetResult);

    private static string Message(string type, string protocol = "fluentcms-preview", int version = 1, string? url = null) =>
        new JsonObject { ["protocol"] = protocol, ["version"] = version, ["type"] = type, ["url"] = url }.ToJsonString();

    private static void Save(IRenderedComponent<AdminApp> app) => ToolbarButton(app, "Save").Click();

    // A change of the title saved, which changes the draft's version and not the locale's status.
    private static void SaveChanged(IRenderedComponent<AdminApp> app)
    {
        var title = app.Find("[data-field='title'] input").GetAttribute("value");
        app.Find("[data-field='title'] input").Input(title + " changed");
        Save(app);
        app.WaitForAssertion(() => app.FindAll(".content-form-saved").ShouldNotBeEmpty());
    }

    private static IElement ToolbarButton(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll(".content-form-toolbar fluent-button").Single(button => button.TextContent.Trim() == text);

    private static string[] Buttons(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-form-history fluent-button").Select(button => button.TextContent.Trim())];

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    private static string[] Notices(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-preview-notices .content-preview-notice").Select(notice => notice.TextContent.Trim())];

    private static string[] Assertive(IRenderedComponent<AdminApp> app) =>
        [.. app.Find("[aria-live=assertive]").Children.Select(message => message.TextContent.Trim()).Where(text => text.Length > 0)];

    private static IElement Pane(IRenderedComponent<AdminApp> app) => app.Find(".content-preview");

    private static IElement Frame(IRenderedComponent<AdminApp> app) => app.Find(".content-preview iframe");

    private static IRenderedComponent<CmsSwitch> Published(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsSwitch>().Single(toggle => toggle.Instance.Label == "Published page");
}
