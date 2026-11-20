using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Content;

// The preview pane of A-29: no button for a type without a template or without preview origins, the draft's page in a frame
// at phone, tablet or full width, the published page while the locale has one, a URL of another origin refused, the page's
// ready taken only from the frame's origin, protocol and version, a save that tells a ready page to refresh and reloads a
// silent one, and a token minted again once it ended.
public sealed class ContentPreviewTests : AdminTestContext
{
    private const string FrontEnd = "https://www.example.com";
    private const string Previewing = """{ "apiBaseUrl": "https://cms.example.com", "previewOrigins": ["https://www.example.com"] }""";

    private readonly FakeContent _content;
    private readonly BunitJSInterop _preview;

    public ContentPreviewTests()
    {
        UseNeutralResources();
        _content = new FakeContent(Management);
        Management.SetTypes(MainSite, Type("post", fields: [Field("title")]));
        _preview = JSInterop.SetupModule(PreviewInterop.ModulePath);
        _preview.SetupModule(invocation => invocation.Identifier == "listen");
        _preview.SetupVoid("post", _ => true).SetVoidResult();
        _content.Preview = (id, _) => ($"{FrontEnd}/posts/{id:D}?preview={{token}}", null);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void ATypeWithoutATemplateShowsNoPreviewButton()
    {
        _content.Preview = (_, _) => (null, null);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });

        var app = Load($"content/post/{id:D}");

        app.WaitForAssertion(() => _content.PreviewMints.ShouldBe(1));
        Buttons(app).ShouldBe(["Version history"]);
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
        app.WaitForAssertion(() => Buttons(app).ShouldContain("Preview"));

        Button(app, "Preview").Click();

        app.WaitForAssertion(() => app.Find(".content-preview-notice").TextContent.ShouldBe("The preview page is not on an origin the admin may show."));
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

        Post(app, FrontEnd, Message("navigated", url: $"{FrontEnd}/about"));
        app.WaitForAssertion(() => app.Find(".content-preview-notice").TextContent.ShouldBe($"Showing {FrontEnd}/about"));
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

    [Fact]
    public void AnEndedTokenIsMintedAgainWhenTheFrameReloads()
    {
        _content.PreviewLifetime = TimeSpan.FromMinutes(-1);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var app = Open(Load($"content/post/{id:D}"));
        Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-2");

        Button(app, "Reload").Click();

        app.WaitForAssertion(() => Frame(app).GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-3"));
    }

    [Fact]
    public void APageThatSaysNoReadyIsShownWithANote()
    {
        Start(Previewing);
        var id = _content.Add("post", new JsonObject { ["title"] = "Hello" });
        var pane = Render<ContentPreview>(parameters => parameters
            .Add(p => p.Type, "post")
            .Add(p => p.EntryId, id)
            .Add(p => p.Version, 1)
            .Add(p => p.ReadyWait, TimeSpan.FromMilliseconds(20)));

        pane.WaitForAssertion(() => pane.Find(".content-preview-notice").TextContent.Trim().ShouldBe("This page does not answer the preview, so each save loads it again."));
        pane.Find("iframe").GetAttribute("src").ShouldBe($"{FrontEnd}/posts/{id:D}?preview=token-1");
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

    private void Post(IRenderedComponent<AdminApp> app, string origin, string message)
    {
        var target = _preview.Invocations.Last(call => call.Identifier == "listen").Arguments[1].ShouldBeOfType<Microsoft.JSInterop.DotNetObjectReference<PreviewInterop.MessageTarget>>();
        app.InvokeAsync(() => target.Value.ReceiveAsync(origin, message)).GetAwaiter().GetResult();
    }

    private static string Message(string type, string protocol = "fluentcms-preview", int version = 1, string? url = null) =>
        new JsonObject { ["protocol"] = protocol, ["version"] = version, ["type"] = type, ["url"] = url }.ToJsonString();

    private static void Save(IRenderedComponent<AdminApp> app) =>
        app.FindAll(".content-form-toolbar fluent-button").Single(button => button.TextContent.Trim() == "Save").Click();

    private static string[] Buttons(IRenderedComponent<AdminApp> app) =>
        [.. app.FindAll(".content-form-history fluent-button").Select(button => button.TextContent.Trim())];

    private static IElement Button(IRenderedComponent<AdminApp> app, string text) =>
        app.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    private static IElement Pane(IRenderedComponent<AdminApp> app) => app.Find(".content-preview");

    private static IElement Frame(IRenderedComponent<AdminApp> app) => app.Find(".content-preview iframe");

    private static IRenderedComponent<CmsSwitch> Published(IRenderedComponent<AdminApp> app) =>
        app.FindComponents<CmsSwitch>().Single(toggle => toggle.Instance.Label == "Published page");
}
