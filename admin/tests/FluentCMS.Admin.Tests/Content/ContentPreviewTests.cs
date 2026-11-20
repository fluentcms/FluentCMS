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
// origin, protocol and version and forgotten when the page navigates or the frame loads another, a save that tells a ready
// page to refresh and reloads a silent one, a token minted again once it ended, and the newest mint's answer used.
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
    }

    [Fact]
    public void AReadyPageIsToldToRefreshAfterASaveAndOtherMessagesAreIgnored()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Post(app, "https://elsewhere.example.com", Message("ready"));
        Post(app, FrontEnd, Message("ready", protocol: "other"));
        Post(app, FrontEnd, Message("ready", version: 2));
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

        // The page that said ready is leaving, so the next save loads the frame unless the next page says it again.
        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/about"));
        app.WaitForAssertion(() => Notices(app).ShouldBe([$"Showing {FrontEnd}/about"]));
        Pane(app).HasAttribute("data-ready").ShouldBeFalse();
    }

    // A ready comes before the load of the page that sent it; a load with none since the one before is of another page.
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
        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2"));
        _preview.Invocations.ShouldNotContain(call => call.Identifier == "post");
    }

    [Fact]
    public void ASaveReloadsAPageThatSaidNothing()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);

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

        ToolbarButton(app, "Publish").Click();

        app.WaitForAssertion(() => Published(app).Instance.Disabled.ShouldBeFalse());
        _content.PreviewMints.ShouldBe(2);
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

    // config.json takes ASCII alone, so a preview origin names an internationalized host in punycode, as the browser writes the
    // origin of a message; a template may write the name in Unicode.
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
    public void AFailedMintAfterASaveLoadsASilentPageAgainWithTheTokenItHas()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        _time.Advance(TimeSpan.FromSeconds(5));
        app.WaitForAssertion(() => Notices(app).ShouldBe([Silent]));
        _content.PreviewUnavailable = true;

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);

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

    // The page is refreshed, so nothing failed that the editor sees; the answer is old, so Reload mints again.
    [Fact]
    public void AFailedMintAfterASaveStillRefreshesAReadyPageWithoutANote()
    {
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Post(app, FrontEnd, Message("ready"));
        app.WaitForAssertion(() => Pane(app).GetAttribute("data-ready").ShouldBe("true"));
        _content.PreviewUnavailable = true;

        app.Find("[data-field='title'] input").Input("Changed");
        Save(app);

        app.WaitForAssertion(() => _preview.Invocations.Count(call => call.Identifier == "post").ShouldBe(1));
        _content.PreviewMints.ShouldBe(2);
        app.FindAll(".content-preview > .content-preview-notice").ShouldBeEmpty();
        Assertive(app).ShouldBeEmpty();

        _content.PreviewUnavailable = false;
        Button(app, "Reload").Click();
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-3"));
    }

    // The mint of a save is answered after the mint of a later Reload; the Reload's answer stays.
    [Fact]
    public void TheNewestMintWins()
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

        held.SetResult();

        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(3));
        Stays(app, () => Frame(app).GetAttribute("src") == $"{FrontEnd}/posts/{id:D}?preview=token-2");
    }

    [Fact]
    public void ClosingThePaneWhileItsMintRunsDropsTheAnswer()
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
        held.SetResult();

        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(1));
        Stays(app, () => Assertive(app).Length == 0);
        _listener.Invocations.ShouldContain(call => call.Identifier == "dispose");
    }

    [Fact]
    public void AnotherEntryOrLocaleWithThePaneOpenIsShownWithATokenOfItsOwn()
    {
        Management.AddLocale(MainSite, "fr", 1);
        var post = Type("post", fields: [Field("title")]);
        post["localized"] = true;
        Management.SetTypes(MainSite, post);
        _content.Preview = (id, locale) => ($"{FrontEnd}/{locale}/posts/{id:D}?preview={{token}}", null);
        var first = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var second = _content.Add("post", new JsonObject { ["title"] = "World" });
        _content.Add("post", new JsonObject { ["title"] = "Monde" }, id: second, locale: "fr");
        var app = Open(Load($"content/post/{first:D}"));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/en/posts/{first:D}?preview=token-1");

        Navigation.NavigateTo($"content/post/{second:D}");
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/en/posts/{second:D}?preview=token-2"));

        Navigation.NavigateTo($"content/post/{second:D}?locale=fr");
        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/fr/posts/{second:D}?preview=token-3"));
        _content.PreviewMints.ShouldBe(3);
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

    [Fact]
    public void ATokenATemplatePutsInThePathIsNotShown()
    {
        _content.Preview = (id, _) => ($"{FrontEnd}/preview/{{token}}/posts/{id:D}", null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));

        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/preview/token-1/about"));

        app.WaitForAssertion(() => Notices(app).ShouldBe([$"Showing {FrontEnd}/preview/…/about"]));
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

    // The id of the element last focused.
    private string? Focused() =>
        JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Select(call => ((ElementReference)call.Arguments[0]!).Id).LastOrDefault();

    // A change that a late answer would make renders within this time; none does.
    private static void Stays(IRenderedComponent<AdminApp> app, Func<bool> holds) =>
        Should.Throw<Bunit.Extensions.WaitForHelpers.WaitForFailedException>(() => app.WaitForState(() => !holds(), TimeSpan.FromMilliseconds(500)));

    private static string Message(string type, string protocol = "fluentcms-preview", int version = 1, string? url = null) =>
        new JsonObject { ["protocol"] = protocol, ["version"] = version, ["type"] = type, ["url"] = url }.ToJsonString();

    private static void Save(IRenderedComponent<AdminApp> app) => ToolbarButton(app, "Save").Click();

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
