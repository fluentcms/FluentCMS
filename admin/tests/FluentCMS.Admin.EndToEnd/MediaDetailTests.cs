using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.EndToEnd.MediaUi;
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
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var tab = await OpenLibraryAsync(browser);
        await UploadAsync(Content(tab), PngFile($"{mark}.png", Red));
        var panel = await OpenAssetAsync(tab, mark);
        var title = panel.GetByLabel(Text("Media.Form.Title"), new LocatorGetByLabelOptions { Exact = true });

        // The metadata is saved together, and the tile shows the new title, which the admin takes from the server's answer.
        await title.FillAsync($"{mark} title");
        await panel.GetByLabel(Text("Media.Form.Caption"), new LocatorGetByLabelOptions { Exact = true }).FillAsync("A red square");
        await panel.GetByLabel(Text("Media.Form.Tags"), new LocatorGetByLabelOptions { Exact = true }).FillAsync("red, square");
        var saved = await tab.AnswerAsync(() => Button(tab, "Media.Form.Save").ClickAsync(), response => response.Request.Method == "PATCH");
        saved.Status.ShouldBe(200);
        var sent = JsonNode.Parse(saved.Request.PostData!)!;
        (sent["title"]!.GetValue<string>(), sent["caption"]!.GetValue<string>()).ShouldBe(($"{mark} title", "A red square"));
        await Expect(tab.Page.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark} title"]);

        // The alt text is saved as the field is left by Tab.
        var alt = panel.GetByLabel(Text("Media.Alt.Label"), new LocatorGetByLabelOptions { Exact = true });
        await alt.FillAsync("A red square");
        var altSaved = await tab.AnswerAsync(() => tab.Page.Keyboard.PressAsync("Tab"), response => response.Request.Method == "PATCH");
        altSaved.Ok.ShouldBeTrue();
        (altSaved.Request.PostData ?? "").ShouldContain("A red square");

        // The focal point moves with the arrow keys on its slider; a slow runner may save between keys, so the save waited for
        // is the one of the last key.
        var across = panel.GetByLabel(Text("Media.Focal.X"), new LocatorGetByLabelOptions { Exact = true });
        await Ui.FocusAsync(across);
        var focal = await tab.AnswerAsync(
            async () =>
            {
                for (var step = 0; step < 5; step++)
                {
                    await tab.Page.Keyboard.PressAsync("ArrowRight");
                }
            },
            response => response.Request.Method == "PATCH" && (response.Request.PostData ?? "").Contains("\"x\":0.55", StringComparison.Ordinal));
        focal.Ok.ShouldBeTrue();
        await Expect(panel.Locator(".media-focal-marker")).ToHaveAttributeAsync("style", new Regex("left:55%"));

        // A replaced file shows its new thumbnail in the open panel.
        var before = await panel.Locator(".media-focal-image img").GetAttributeAsync("src");
        await panel.Locator("input[type=file]").SetInputFilesAsync(PngFile($"{mark}-wide.png", Halves));
        await Expect(panel.Locator(".media-panel-facts")).ToContainTextAsync($"{mark}-wide.png");
        await Expect(panel.Locator(".media-focal-image img")).Not.ToHaveAttributeAsync("src", before ?? "");
        await Expect(panel.Locator(".media-panel-facts")).ToContainTextAsync("4 × 2");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The save goes with the version the panel read as If-Match, and the server's 412 is the conflict; nothing reads the
    // asset before the save.
    [Fact]
    public async Task ASaveAfterAnotherTabChangedTheAssetShowsTheConflict()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var mine = await OpenLibraryAsync(browser);
        await UploadAsync(Content(mine), PngFile($"{mark}.png", Red));
        var panel = await OpenAssetAsync(mine, mark);

        // The second tab signs in through the server's session, and saves a title.
        var theirs = await browser.NewTabAsync();
        await theirs.Page.GotoAsync(new Uri(Host.Admin, "media").AbsoluteUri);
        await Expect(Content(theirs).GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();
        var theirPanel = await OpenAssetAsync(theirs, mark);
        var theirTitle = theirPanel.GetByLabel(Text("Media.Form.Title"), new LocatorGetByLabelOptions { Exact = true });
        await theirTitle.FillAsync($"{mark} theirs");
        await Expect(theirTitle).ToHaveValueAsync($"{mark} theirs");
        var theirSave = await theirs.AnswerAsync(() => Button(theirs, "Media.Form.Save").ClickAsync(), response => response.Request.Method == "PATCH");
        theirSave.Status.ShouldBe(200);

        // The reads of the asset itself, GET /media/{id} with nothing after the id, from here up to the save; the conflict
        // reads it after the 412. A read answered 401, which a token five seconds old gets and the admin sends again, is not
        // counted. The panel's usage has loaded first, so that no read of the panel's opening is still under way.
        await Expect(panel.Locator(".media-panel-usage")).Not.ToContainTextAsync(Text("Media.Usage.Loading"));
        var assetRead = new Regex(@"/api/management/v1/media/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(\?|$)");
        List<IRequest> reads = [];
        var sent = false;
        mine.Page.Request += (_, request) =>
        {
            if (request.Method == "PATCH")
            {
                sent = true;
            }
            else if (!sent && request.Method == "GET" && assetRead.IsMatch(request.Url))
            {
                reads.Add(request);
            }
        };
        // The caption is checked as filled right before the save, so that a render between the two cannot drop it unseen.
        var caption = panel.GetByLabel(Text("Media.Form.Caption"), new LocatorGetByLabelOptions { Exact = true });
        await caption.FillAsync("Mine");
        await Expect(caption).ToHaveValueAsync("Mine");
        var refused = await mine.AnswerAsync(() => Button(mine, "Media.Form.Save").ClickAsync(), response => response.Request.Method == "PATCH");
        refused.Status.ShouldBe(412);
        (await refused.Request.HeaderValueAsync("if-match")).ShouldNotBeNullOrEmpty();
        (refused.Request.PostData ?? "").ShouldContain("Mine");
        foreach (var read in reads)
        {
            ((await read.ResponseAsync())?.Status).ShouldBe(401, read.Url);
        }

        await Expect(panel.Locator(".media-save-conflict")).ToContainTextAsync(Text("Media.Conflict.Text"));
        await Button(mine, "Media.Conflict.Theirs").ClickAsync();
        await Expect(panel.GetByLabel(Text("Media.Form.Title"), new LocatorGetByLabelOptions { Exact = true })).ToHaveValueAsync($"{mark} theirs");
        await Expect(panel.Locator(".media-save-conflict")).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAdministratorAddsAndDeletesAPresetAndAnEditorIsNotOfferedThePage()
    {
        var name = Mark;
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await SignedInTabAsync(browser, "media/presets", admin);
        var page = Content(tab);
        await Expect(page.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Presets.Heading"), Exact = true, Level = 1 })).ToBeVisibleAsync();

        // A required field's label ends with its mark.
        await page.GetByLabel(new Regex($"^{Text("Media.Presets.Name")}")).FillAsync(name);
        await page.GetByLabel(Text("Media.Presets.Width"), new LocatorGetByLabelOptions { Exact = true }).FillAsync("400");
        await page.GetByLabel(Text("Media.Presets.Fit"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync("cover");
        await Button(tab, "Media.Presets.Add").ClickAsync();
        var row = page.Locator(".media-presets-table tbody tr").Filter(new LocatorFilterOptions { HasText = name });
        await Expect(row).ToContainTextAsync(Text("Media.Presets.Fit.cover"));

        await tab.OpenDialogAsync(() => row.Locator($"fluent-button[aria-label='{Text("Media.Presets.Delete", name)}']").ClickAsync(), Text("Media.Presets.DeleteTitle"));
        await Button(tab, "Media.Presets.DeleteConfirm").ClickAsync();
        await Expect(row).ToHaveCountAsync(0);

        var editorBrowser = await OpenBrowserAsync();
        var editorTab = await SignedInTabAsync(editorBrowser, "media");
        await Expect(Content(editorTab).GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();
        await Expect(editorTab.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = Text("Media.Presets.Menu"), Exact = true })).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
        editorBrowser.PolicyViolations.ShouldBeEmpty();
    }

    private async Task<AdminTab> OpenLibraryAsync(AdminBrowser browser)
    {
        var tab = await SignedInTabAsync(browser, "media");
        await Expect(Content(tab).GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();
        return tab;
    }

    // Finds the asset by its mark and opens its detail.
    private static async Task<ILocator> OpenAssetAsync(AdminTab tab, string mark)
    {
        await SearchAsync(Content(tab), mark);
        await Expect(tab.Page.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark}.png"]);
        await tab.Page.Locator(".media-tile-open").ClickAsync();
        var panel = tab.Page.Locator(".cms-drawer");
        await Expect(panel.GetByLabel(Text("Media.Form.Title"), new LocatorGetByLabelOptions { Exact = true })).ToBeVisibleAsync();
        return panel;
    }

    private static ILocator Content(AdminTab tab) => tab.Page.Locator("main");

}
