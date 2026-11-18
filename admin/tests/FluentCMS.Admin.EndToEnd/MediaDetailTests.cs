using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The detail of an asset (A-50) on the reference host: metadata edited, the focal point set by keyboard, the file replaced,
// and the presets page. The host is shared, so every asset and preset a case makes is its own.
public sealed class MediaDetailTests(EndToEndHost host) : BrowserTest(host)
{
    // Two by two red pixels, and four by two pixels half black and half white.
    private const string Red = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGP4z8AARAwQCgAf7gP9i18U1AAAAABJRU5ErkJggg==";
    private const string Halves = "iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAEUlEQVR4nGNgAIP/YMCAzAEAd6IL9cTZu14AAAAASUVORK5CYII=";

    [Fact]
    public async Task TheEditorEditsTheMetadataSetsTheFocalPointByKeyboardAndReplacesTheFile()
    {
        var mark = "d" + Guid.NewGuid().ToString("N")[..8];
        var browser = await OpenBrowserAsync();
        var tab = await OpenLibraryAsync(browser, Host.Editor);
        await tab.Page.Locator("[data-media-upload] input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = $"{mark}.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Red) });
        await Content(tab).GetByLabel(Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true }).FillAsync(mark);
        await MediaButton(tab, "Media.Search.Submit").ClickAsync();
        await Expect(tab.Page.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark}.png"]);

        await tab.Page.Locator(".media-tile-open").ClickAsync();
        var panel = tab.Page.Locator(".cms-drawer");
        var title = panel.GetByLabel(Text("Media.Form.Title"), new LocatorGetByLabelOptions { Exact = true });
        await Expect(title).ToBeVisibleAsync();

        // The metadata is saved together, and the tile shows the new title.
        await title.FillAsync($"{mark} title");
        await panel.GetByLabel(Text("Media.Form.Caption"), new LocatorGetByLabelOptions { Exact = true }).FillAsync("A red square");
        await panel.GetByLabel(Text("Media.Form.Tags"), new LocatorGetByLabelOptions { Exact = true }).FillAsync("red, square");
        var saved = await tab.Page.RunAndWaitForResponseAsync(
            () => MediaButton(tab, "Media.Form.Save").ClickAsync(),
            response => response.Request.Method == "PATCH",
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        var body = JsonNode.Parse(await saved.TextAsync())!;
        (body["title"]!.GetValue<string>(), body["caption"]!.GetValue<string>()).ShouldBe(($"{mark} title", "A red square"));
        await Expect(tab.Page.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark} title"]);

        // The alt text is saved as the field is left by Tab.
        var alt = panel.GetByLabel(Text("Media.Alt.Label"), new LocatorGetByLabelOptions { Exact = true });
        await alt.FillAsync("A red square");
        var altSaved = await tab.Page.RunAndWaitForResponseAsync(
            () => tab.Page.Keyboard.PressAsync("Tab"),
            response => response.Request.Method == "PATCH",
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        // The body of an answer may be gone from the browser by the time it is read, so the request and the status are read.
        altSaved.Ok.ShouldBeTrue();
        (altSaved.Request.PostData ?? "").ShouldContain("A red square");

        // The focal point moves with the arrow keys on its slider; a slow runner may save between keys, so the save waited for
        // is the one of the last key.
        var across = panel.GetByLabel(Text("Media.Focal.X"), new LocatorGetByLabelOptions { Exact = true });
        await across.FocusAsync();
        var focal = await tab.Page.RunAndWaitForResponseAsync(
            async () =>
            {
                for (var step = 0; step < 5; step++)
                {
                    await tab.Page.Keyboard.PressAsync("ArrowRight");
                }
            },
            response => response.Request.Method == "PATCH" && (response.Request.PostData ?? "").Contains("\"x\":0.55", StringComparison.Ordinal),
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        focal.Ok.ShouldBeTrue();
        await Expect(panel.Locator(".media-focal-marker")).ToHaveAttributeAsync("style", new Regex("left:55%"));

        // A replaced file shows its new thumbnail in the open panel.
        var before = await panel.Locator(".media-focal-image img").GetAttributeAsync("src");
        await panel.Locator("input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = $"{mark}-wide.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Halves) });
        await Expect(panel.Locator(".media-panel-facts")).ToContainTextAsync($"{mark}-wide.png");
        (await panel.Locator(".media-focal-image img").GetAttributeAsync("src")).ShouldNotBe(before);
        await Expect(panel.Locator(".media-panel-facts")).ToContainTextAsync("4 × 2");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAdministratorAddsAndDeletesAPresetAndAnEditorIsNotOfferedThePage()
    {
        var name = "p" + Guid.NewGuid().ToString("N")[..8];
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "media/presets").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, admin);
        var page = Content(tab);
        await Expect(page.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Presets.Heading"), Exact = true, Level = 1 })).ToBeVisibleAsync();

        // A required field's label ends with its mark.
        await page.GetByLabel(new Regex($"^{Text("Media.Presets.Name")}")).FillAsync(name);
        await page.GetByLabel(Text("Media.Presets.Width"), new LocatorGetByLabelOptions { Exact = true }).FillAsync("400");
        await page.GetByLabel(Text("Media.Presets.Fit"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync("cover");
        await MediaButton(tab, "Media.Presets.Add").ClickAsync();
        var row = page.Locator(".media-presets-table tbody tr").Filter(new LocatorFilterOptions { HasText = name });
        await Expect(row).ToContainTextAsync(Text("Media.Presets.Fit.cover"));

        await row.Locator($"fluent-button[aria-label='{Text("Media.Presets.Delete", name)}']").ClickAsync();
        await MediaButton(tab, "Media.Presets.DeleteConfirm").ClickAsync();
        await Expect(row).ToHaveCountAsync(0);

        var editorBrowser = await OpenBrowserAsync();
        var editorTab = await editorBrowser.NewTabAsync();
        await editorTab.Page.GotoAsync(new Uri(Host.Admin, "media").AbsoluteUri);
        await editorBrowser.SignInOnTheHostedPageAsync(editorTab, Host.Editor);
        await Expect(Content(editorTab).GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();
        await Expect(editorTab.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = Text("Media.Presets.Menu"), Exact = true })).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
        editorBrowser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key, params string[] arguments)
    {
        var text = Repository.MediaText(key);
        for (var index = 0; index < arguments.Length; index++)
        {
            text = text.Replace($"{{{index}}}", arguments[index], StringComparison.Ordinal);
        }

        return text;
    }

    private async Task<AdminTab> OpenLibraryAsync(AdminBrowser browser, Editor user)
    {
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "media").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, user);
        await Expect(Content(tab).GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();
        return tab;
    }

    private static ILocator Content(AdminTab tab) => tab.Page.Locator("main");

    private static ILocator MediaButton(AdminTab tab, string key, params string[] arguments) =>
        tab.Page.Locator("fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Text(key, arguments))}\s*$"), Visible = true });
}
