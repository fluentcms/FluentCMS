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
// version 1 of the postMessage contract; a save tells the page to refresh, and the page shows it without loading again. The
// pane takes focus as it opens, its tablet width fits any window, and a message of another window than its frame's is ignored.
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
        var tab = await SignedInTabAsync(browser, $"content/showcase/{id:D}");
        var loads = 0;
        var admin = Host.Origin.GetLeftPart(UriPartial.Authority);
        await tab.Page.RouteAsync($"{EndToEndHost.PreviewOrigin}/**", route =>
        {
            Interlocked.Increment(ref loads);
            return route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html", Body = Page(admin) });
        });

        var open = Ui.ButtonIn(tab.Page.Locator(".content-form-history"), Repository.AdminText("Content.Preview.Open"));
        await Ui.PressAsync(open, "Enter");

        var pane = tab.Page.Locator(".content-preview");
        var frame = pane.Locator("iframe");
        await Expect(frame).ToHaveAttributeAsync("src", new Regex($"^{Regex.Escape(EndToEndHost.PreviewOrigin)}/showcase/{id:D}\\?preview=[^&]+$"));
        await Expect(frame).ToHaveCSSAsync("width", "390px");
        await Expect(pane).ToBeFocusedAsync();
        await Expect(pane).ToHaveAttributeAsync("data-ready", "true");
        (await ControlledAsync(tab.Page, Repository.AdminText("Content.Preview.Open"))).ShouldBe(["content-preview"]);
        var page = tab.Page.FrameLocator(".content-preview iframe");
        await Expect(page.Locator("#state")).ToHaveTextAsync("ready");

        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Changed {Mark}");
        var save = tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Form.Save"))}\s*$") });
        var saved = await tab.AnswerAsync(() => Ui.PressAsync(save, "Enter"), "PUT", tab.ManagementUrl($"content/showcase/{id:D}"));
        saved.Status.ShouldBe(200);

        await Expect(page.Locator("#state")).ToHaveTextAsync("refresh 2");
        Volatile.Read(ref loads).ShouldBe(1);

        // In a window wide enough for the sidebar, 20rem of form and 820 pixels, the tablet width sits beside the form as the
        // phone's does; full width puts the pane below it, as wide as it.
        await tab.Page.SetViewportSizeAsync(1600, 900);
        var width = tab.Page.GetByLabel(Repository.AdminText("Content.Preview.Width"), new PageGetByLabelOptions { Exact = true });
        var layout = tab.Page.Locator(".content-form-layout");
        await width.SelectOptionAsync("tablet");
        await Expect(frame).ToHaveCSSAsync("width", "820px");
        await Expect(layout).ToHaveAttributeAsync("data-preview", "beside");

        // In a narrower window the pane goes below the form, and the tablet's frame stays within the pane and the pane within
        // the form's area and the window.
        foreach (var window in new[] { 1400, 1100, 900, 600 })
        {
            await tab.Page.SetViewportSizeAsync(window, 900);
            var shown = (await frame.BoundingBoxAsync()).ShouldNotBeNull();
            var box = (await pane.BoundingBoxAsync()).ShouldNotBeNull();
            var room = (await layout.BoundingBoxAsync()).ShouldNotBeNull();
            (shown.X + shown.Width).ShouldBeLessThanOrEqualTo(box.X + box.Width + 1, $"the frame at {window} pixels");
            (box.X + box.Width).ShouldBeLessThanOrEqualTo(room.X + room.Width + 1, $"the pane at {window} pixels");
            (room.X + room.Width).ShouldBeLessThanOrEqualTo(window, $"the form's area at {window} pixels");
            (await pane.EvaluateAsync<bool>("pane => pane.scrollWidth <= pane.clientWidth")).ShouldBeTrue($"the pane scrolls at {window} pixels");
        }

        await tab.Page.SetViewportSizeAsync(1600, 900);
        await width.SelectOptionAsync("full");
        await Expect(layout).ToHaveAttributeAsync("data-preview", "full");
        var full = (await frame.BoundingBoxAsync()).ShouldNotBeNull();
        var area = (await layout.BoundingBoxAsync()).ShouldNotBeNull();
        full.Width.ShouldBeGreaterThan(820);
        full.Width.ShouldBe(area.Width, 2);
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
        var tab = await SignedInTabAsync(browser, $"content/showcase/{id:D}");
        var admin = Host.Origin.GetLeftPart(UriPartial.Authority);
        await tab.Page.RouteAsync($"{EndToEndHost.PreviewOrigin}/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html",
            Body = route.Request.Url.EndsWith("/nested", StringComparison.Ordinal) ? Nested(admin) : """<!doctype html><title>Showcase</title><iframe src="/nested"></iframe>""",
        }));

        var open = Ui.ButtonIn(tab.Page.Locator(".content-form-history"), Repository.AdminText("Content.Preview.Open"));
        await Ui.PressAsync(open, "Enter");

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

    // The page a front end serves for a preview, reduced to the contract: it says ready to the admin's origin alone, with no
    // token, and shows each refresh it is told of.
    private static string Page(string admin) => $$"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Showcase</title></head>
        <body>
        <p id="state">loading</p>
        <script>
        const admin = {{System.Text.Json.JsonSerializer.Serialize(admin)}};
        addEventListener("message", event => {
            const message = event.data;
            if (event.origin !== admin || message?.protocol !== "fluentcms-preview" || message.version !== 1) {
                return;
            }

            if (message.type === "refresh") {
                document.getElementById("state").textContent = "refresh " + message.entry.version;
            }
        });
        parent.postMessage({ protocol: "fluentcms-preview", version: 1, type: "ready", url: location.origin + location.pathname }, admin);
        document.getElementById("state").textContent = "ready";
        </script>
        </body>
        </html>
        """;

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
