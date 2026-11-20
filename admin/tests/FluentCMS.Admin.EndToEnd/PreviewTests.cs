using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The preview of A-29 for the class's editor: a draft of showcase, the one type with a preview template, is shown in the pane
// at phone, tablet and full width from the preview origin of config.json, which the browser answers with a page that speaks
// version 1 of the postMessage contract; a save tells the page to refresh, and the page shows it without loading again.
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

        var open = tab.Page.Locator(".content-form-history fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Preview.Open"))}\s*$") });
        await Ui.PressAsync(open, "Enter");

        var pane = tab.Page.Locator(".content-preview");
        var frame = pane.Locator("iframe");
        await Expect(frame).ToHaveAttributeAsync("src", new Regex($"^{Regex.Escape(EndToEndHost.PreviewOrigin)}/showcase/{id:D}\\?preview=[^&]+$"));
        await Expect(frame).ToHaveCSSAsync("width", "390px");
        await Expect(pane).ToHaveAttributeAsync("data-ready", "true");
        var page = tab.Page.FrameLocator(".content-preview iframe");
        await Expect(page.Locator("#state")).ToHaveTextAsync("ready");

        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Changed {Mark}");
        var save = tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Form.Save"))}\s*$") });
        var saved = await tab.AnswerAsync(() => Ui.PressAsync(save, "Enter"), "PUT", tab.ManagementUrl($"content/showcase/{id:D}"));
        saved.Status.ShouldBe(200);

        await Expect(page.Locator("#state")).ToHaveTextAsync("refresh 2");
        Volatile.Read(ref loads).ShouldBe(1);

        // The tablet width sits beside the form as the phone's does; full width puts the pane above it, as wide as it.
        var width = tab.Page.GetByLabel(Repository.AdminText("Content.Preview.Width"), new PageGetByLabelOptions { Exact = true });
        var layout = tab.Page.Locator(".content-form-layout");
        await width.SelectOptionAsync("tablet");
        await Expect(frame).ToHaveCSSAsync("width", "820px");
        await Expect(layout).ToHaveAttributeAsync("data-preview", "beside");
        await width.SelectOptionAsync("full");
        await Expect(layout).ToHaveAttributeAsync("data-preview", "full");
        var full = (await frame.BoundingBoxAsync()).ShouldNotBeNull();
        var area = (await layout.BoundingBoxAsync()).ShouldNotBeNull();
        full.Width.ShouldBeGreaterThan(820);
        full.Width.ShouldBe(area.Width, 2);
        browser.PolicyViolations.ShouldBeEmpty();
    }

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
