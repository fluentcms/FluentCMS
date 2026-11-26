using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The preview of A-29 for the class's editor: a draft of showcase, the one type with a preview template, is shown in the pane
// at phone, tablet and full width from the preview origin of config.json, which the browser answers with a page that speaks
// the postMessage contract; a save tells the page to refresh, and the page shows it without loading again. The pane takes
// focus as it opens, sits beside the form or below it and fits any window, forgets ready when a page of version 2 navigates
// to one that does not say it, is ready again when it navigates to one that answers hello, and ignores a message of another
// window than its frame's.
public sealed class PreviewTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Template = EndToEndHost.PreviewOrigin + "/showcase/{id}?preview={token}";

    [Fact]
    public async Task TheEditorPreviewsADraftAndASaveRefreshesThePage()
    {
        using var api = await Host.ManagementClientAsync();
        await SetTemplateAsync(api);
        var id = await CreateAsync(api, $"Showcase {Mark}");

        var browser = await OpenBrowserAsync();
        var loads = 0;
        var admin = Host.Origin.GetLeftPart(UriPartial.Authority);
        var tab = await EntryTabAsync(browser, id, route =>
        {
            Interlocked.Increment(ref loads);
            return route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html", Body = Page(admin, 1) });
        });

        await Ui.PressAsync(Open(tab), "Enter");

        var pane = tab.Page.Locator(".content-preview");
        var frame = pane.Locator("iframe");
        await Expect(frame).ToHaveAttributeAsync("src", new Regex($"^{Regex.Escape(EndToEndHost.PreviewOrigin)}/showcase/{id:D}\\?preview=[^&]+$"));
        await Expect(frame).ToHaveCSSAsync("width", "390px");
        await Expect(pane).ToBeFocusedAsync();
        await Expect(pane).ToHaveAttributeAsync("data-ready", "true");
        // The accessibility tree is read through Chromium's DevTools protocol, which the other browsers do not have.
        if (Host.Engine.IsChromium)
        {
            (await ControlledAsync(tab.Page, Repository.AdminText("Content.Preview.Open"))).ShouldBe(["content-preview"]);
        }

        var page = tab.Page.FrameLocator(".content-preview iframe");
        await Expect(page.Locator("#state")).ToHaveTextAsync("ready");

        await SaveAsync(tab, id);

        await Expect(page.Locator("#state")).ToHaveTextAsync("refresh 2");
        Volatile.Read(ref loads).ShouldBe(1);

        // The pane sits beside the form in a window wide enough for the sidebar, 20rem of form and the frame, from 68rem at
        // phone width and from 95rem at tablet width, and below it otherwise; the frame stays within the pane and the pane within
        // the form's area and the window. Each width is measured on both sides of its breakpoint.
        var width = tab.Page.GetByLabel(Repository.AdminText("Content.Preview.Width"), new PageGetByLabelOptions { Exact = true });
        var layout = tab.Page.Locator(".content-form-layout");
        var form = layout.Locator(".content-form");
        (string Width, int Window, bool Beside)[] windows =
        [
            ("tablet", 1600, true), ("tablet", 1530, true), ("tablet", 1500, false), ("tablet", 1100, false), ("tablet", 900, false), ("tablet", 600, false),
            ("phone", 1100, true), ("phone", 1080, false),
        ];
        foreach (var (size, window, beside) in windows)
        {
            await tab.Page.SetViewportSizeAsync(window, 900);
            await width.SelectOptionAsync(size);
            await Expect(pane).ToHaveAttributeAsync("data-width", size);
            var at = $"{size} at {window} pixels";
            if (beside)
            {
                await Expect(frame).ToHaveCSSAsync("width", size == "tablet" ? "820px" : "390px");
            }

            var shown = (await frame.BoundingBoxAsync()).ShouldNotBeNull();
            var box = (await pane.BoundingBoxAsync()).ShouldNotBeNull();
            var room = (await layout.BoundingBoxAsync()).ShouldNotBeNull();
            var fields = (await form.BoundingBoxAsync()).ShouldNotBeNull();
            (shown.X + shown.Width).ShouldBeLessThanOrEqualTo(box.X + box.Width + 1, $"the frame, {at}");
            (box.X + box.Width).ShouldBeLessThanOrEqualTo(room.X + room.Width + 1, $"the pane, {at}");
            (room.X + room.Width).ShouldBeLessThanOrEqualTo(window, $"the form's area, {at}");
            (await pane.EvaluateAsync<bool>("pane => pane.scrollWidth <= pane.clientWidth")).ShouldBeTrue($"the pane scrolls, {at}");
            if (beside)
            {
                box.X.ShouldBeGreaterThanOrEqualTo(fields.X + fields.Width - 1, $"the pane is beside the form, {at}");
            }
            else
            {
                box.Y.ShouldBeGreaterThanOrEqualTo(fields.Y + fields.Height - 1, $"the pane is below the form, {at}");
            }
        }

        // Full width puts the pane below the form, as wide as it.
        await tab.Page.SetViewportSizeAsync(1600, 900);
        await width.SelectOptionAsync("full");
        await Expect(layout).ToHaveAttributeAsync("data-preview", "full");
        var full = (await frame.BoundingBoxAsync()).ShouldNotBeNull();
        var area = (await layout.BoundingBoxAsync()).ShouldNotBeNull();
        var above = (await form.BoundingBoxAsync()).ShouldNotBeNull();
        full.Width.ShouldBeGreaterThan(820);
        full.Width.ShouldBe(area.Width, 2);
        (await pane.BoundingBoxAsync()).ShouldNotBeNull().Y.ShouldBeGreaterThanOrEqualTo(above.Y + above.Height - 1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The page of version 2 said ready, then the editor follows its link to a page without the script, whose load is the
    // frame's next: the pane forgets ready, whichever of the page's ready and the frame's load the browser reported first,
    // gives the five seconds again, and the next save loads the frame instead of telling it to refresh.
    [Fact]
    public async Task APageThatNavigatesToOneWithoutTheContractIsLoadedAgainOnASave()
    {
        using var api = await Host.ManagementClientAsync();
        await SetTemplateAsync(api);
        var id = await CreateAsync(api, $"Showcase {Mark}");

        var browser = await OpenBrowserAsync();
        var loads = 0;
        var admin = Host.Origin.GetLeftPart(UriPartial.Authority);
        var tab = await EntryTabAsync(browser, id, route =>
        {
            Interlocked.Increment(ref loads);
            return route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html",
                Body = route.Request.Url.EndsWith("/plain", StringComparison.Ordinal) ? """<!doctype html><title>Plain</title><p id="state">plain</p>""" : Page(admin, 2),
            });
        });

        await Ui.PressAsync(Open(tab), "Enter");
        var pane = tab.Page.Locator(".content-preview");
        var page = tab.Page.FrameLocator(".content-preview iframe");
        await Expect(pane).ToHaveAttributeAsync("data-ready", "true");
        await Expect(page.Locator("#state")).ToHaveTextAsync("ready");

        await page.Locator("#away").ClickAsync();

        await Expect(page.Locator("#state")).ToHaveTextAsync("plain");
        await Expect(pane).Not.ToHaveAttributeAsync("data-ready", "true");
        await Expect(pane.Locator(".content-preview-notice")).ToHaveTextAsync(Repository.AdminText("Content.Preview.Silent"));
        Volatile.Read(ref loads).ShouldBe(2);

        // The save is waited for by what it does to the page, the frame loaded again: after the frame's own navigation the
        // browser did not always report the answer to the save, which the form had shown as saved.
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Changed {Mark}");
        await Ui.PressAsync(Ui.ButtonIn(tab.Page.Locator(".content-form-toolbar"), Repository.AdminText("Content.Form.Save")), "Enter");

        await Expect(page.Locator("#state")).ToHaveTextAsync("ready");
        Volatile.Read(ref loads).ShouldBe(3);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The page of version 2 said ready, then the editor follows its link to another page of version 2, which says ready and
    // answers the hello after its load: the pane is ready again, and the next save tells it to refresh without a load.
    [Fact]
    public async Task APageThatNavigatesToOneWithTheContractIsRefreshedOnASave()
    {
        using var api = await Host.ManagementClientAsync();
        await SetTemplateAsync(api);
        var id = await CreateAsync(api, $"Showcase {Mark}");

        var browser = await OpenBrowserAsync();
        var loads = 0;
        var admin = Host.Origin.GetLeftPart(UriPartial.Authority);
        var tab = await EntryTabAsync(browser, id, route =>
        {
            Interlocked.Increment(ref loads);
            return route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "text/html",
                Body = Page(admin, 2, route.Request.Url.EndsWith("/onward", StringComparison.Ordinal) ? "onward" : "showcase"),
            });
        });

        await Ui.PressAsync(Open(tab), "Enter");
        var pane = tab.Page.Locator(".content-preview");
        var page = tab.Page.FrameLocator(".content-preview iframe");
        await Expect(pane).ToHaveAttributeAsync("data-ready", "true");

        await page.Locator("#onward").ClickAsync();

        // The pane ends the ready state before it says hello, so a ready once the page heard it is the page's answer.
        await Expect(page.Locator("#where")).ToHaveTextAsync("onward");
        await Expect(page.Locator("body")).ToHaveAttributeAsync("data-heard", "hello");
        await Expect(pane).ToHaveAttributeAsync("data-ready", "true");
        Volatile.Read(ref loads).ShouldBe(2);

        await SaveAsync(tab, id);

        await Expect(page.Locator("#state")).ToHaveTextAsync("refresh 2");
        await Expect(page.Locator("#where")).ToHaveTextAsync("onward");
        Volatile.Read(ref loads).ShouldBe(2);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A frame inside the page is of the preview origin too, so only its window tells its ready from the page's, which says none.
    [Fact]
    public async Task AReadyFromAnotherWindowOfThePreviewOriginIsIgnored()
    {
        using var api = await Host.ManagementClientAsync();
        await SetTemplateAsync(api);
        var id = await CreateAsync(api, $"Showcase {Mark}");

        var browser = await OpenBrowserAsync();
        var admin = Host.Origin.GetLeftPart(UriPartial.Authority);
        var tab = await EntryTabAsync(browser, id, route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html",
            Body = route.Request.Url.EndsWith("/nested", StringComparison.Ordinal) ? Nested(admin) : """<!doctype html><title>Showcase</title><iframe src="/nested"></iframe>""",
        }));

        await Ui.PressAsync(Open(tab), "Enter");

        var pane = tab.Page.Locator(".content-preview");
        await Expect(tab.Page.FrameLocator(".content-preview iframe").FrameLocator("iframe").Locator("#state")).ToHaveTextAsync("posted");
        await Expect(pane.Locator(".content-preview-notice")).ToHaveTextAsync(Repository.AdminText("Content.Preview.Silent"));
        await Expect(pane).Not.ToHaveAttributeAsync("data-ready", "true");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Nested(string admin) => $$"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Nested</title></head>
        <body>
        <p id="state">loading</p>
        <script>
        top.postMessage({ protocol: "fluentcms-preview", version: 1, type: "ready", url: location.href }, {{System.Text.Json.JsonSerializer.Serialize(admin)}});
        document.getElementById("state").textContent = "posted";
        </script>
        </body>
        </html>
        """;

    // The page a front end serves for a preview, reduced to the contract of the version given: it says ready to the admin's
    // origin alone, with no token, shows each refresh it is told of, from version 2 answers hello and marks that it heard it,
    // and links to a page without the script and to another with it.
    private static string Page(string admin, int version, string where = "showcase") => $$"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Showcase</title></head>
        <body>
        <p id="where">{{where}}</p>
        <p id="state">loading</p>
        <script>
        const admin = {{System.Text.Json.JsonSerializer.Serialize(admin)}};
        const version = {{version}};
        const ready = () => parent.postMessage({ protocol: "fluentcms-preview", version, type: "ready", url: location.origin + location.pathname }, admin);
        addEventListener("message", event => {
            const message = event.data;
            if (event.origin !== admin || message?.protocol !== "fluentcms-preview" || !(message.version >= 1 && message.version <= version)) {
                return;
            }

            if (message.type === "refresh") {
                document.getElementById("state").textContent = "refresh " + message.entry.version;
            } else if (message.type === "hello") {
                document.body.dataset.heard = "hello";
                ready();
            }
        });
        ready();
        document.getElementById("state").textContent = "ready";
        </script>
        <a id="away" href="/plain">Away</a>
        <a id="onward" href="/onward">Onward</a>
        </body>
        </html>
        """;

    // A tab signed in at the entry, whose preview origin the handler answers. The route is put on once the form shows: one put
    // on while the return from the sign-in navigated lost the browser's reports of the page's calls from then on, the save's
    // answer among them, though the page made them and the host answered (#3654).
    private async Task<AdminTab> EntryTabAsync(AdminBrowser browser, Guid id, Func<IRoute, Task> handler)
    {
        var tab = await SignedInTabAsync(browser, $"content/showcase/{id:D}");
        await Expect(Open(tab)).ToBeVisibleAsync();
        await tab.Page.RouteAsync($"{EndToEndHost.PreviewOrigin}/**", handler);
        return tab;
    }

    private static ILocator Open(AdminTab tab) => Ui.ButtonIn(tab.Page.Locator(".content-form-history"), Repository.AdminText("Content.Preview.Open"));

    private static async Task SaveAsync(AdminTab tab, Guid id)
    {
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Changed {Guid.NewGuid():N}");
        var save = Ui.ButtonIn(tab.Page.Locator(".content-form-toolbar"), Repository.AdminText("Content.Form.Save"));
        var saved = await tab.AnswerAsync(() => Ui.PressAsync(save, "Enter"), "PUT", tab.ManagementUrl($"content/showcase/{id:D}"));
        saved.Status.ShouldBe(200);
    }

    // The ids the browser's accessibility tree resolves a button's aria-controls to; the button of the component library is in
    // a shadow root, which could hide them.
    private static async Task<string[]> ControlledAsync(IPage page, string button)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        var tree = (await cdp.SendAsync("Accessibility.getFullAXTree")).ShouldNotBeNull();
        await cdp.DetachAsync();
        return [.. tree.GetProperty("nodes").EnumerateArray()
            .Where(node => Value(node, "role") == "button" && Value(node, "name") == button && node.TryGetProperty("properties", out _))
            .SelectMany(node => node.GetProperty("properties").EnumerateArray())
            .Where(property => property.GetProperty("name").GetString() == "controls" && property.GetProperty("value").TryGetProperty("relatedNodes", out _))
            .SelectMany(property => property.GetProperty("value").GetProperty("relatedNodes").EnumerateArray())
            .Select(related => related.GetProperty("idref").GetString() ?? "")];
    }

    private static string? Value(JsonElement node, string member) =>
        node.TryGetProperty(member, out var found) && found.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // The instance's defaults, so that every site has the template; a run that set it before writes it again on its version.
    private static async Task SetTemplateAsync(HttpClient api)
    {
        const string area = "api/management/v1/settings/preview/defaults";
        using var read = await api.GetAsync(new Uri(area, UriKind.Relative), TestContext.Current.CancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = new JsonObject { ["templates"] = new JsonObject { ["showcase"] = Template } }.ToJsonString();
        using var write = new HttpRequestMessage(HttpMethod.Put, new Uri(area, UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        write.Headers.IfMatch.Add(read.Headers.ETag ?? new EntityTagHeaderValue("\"0\""));
        using var written = await api.SendAsync(write, TestContext.Current.CancellationToken);
        written.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateAsync(HttpClient api, string title)
    {
        using var created = await api.PostAsync(
            new Uri("api/management/v1/content/showcase", UriKind.Relative),
            new StringContent(new JsonObject { ["title"] = title }.ToJsonString(), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var location = created.Headers.Location.ShouldNotBeNull().OriginalString;
        return Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }
}
