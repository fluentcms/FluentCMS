using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The media field, its list cell and the media picker of A-20 on the Blog sample's Article.Hero. The host is shared, so a
// case finds the sample's image by a search and names what it makes with a mark of its own.
public sealed class MediaFieldTests(EndToEndHost host) : BrowserTest(host)
{
    // A PNG of one pixel.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    // The image the Blog sample seeds, by its title.
    private const string Welcome = "Welcome";

    [Fact]
    public async Task TheEditorUploadsFromThePickerRemovesItChoosesTheSamplesImageAndTheListShowsIt()
    {
        var mark = Mark();
        var browser = await OpenBrowserAsync();
        var tab = await NewArticleAsync(browser, Host.Editor, mark);

        // An image uploaded from the picker, then chosen there.
        await FieldButton(tab, "Media.Field.Choose").ClickAsync();
        var picker = Picker(tab);
        await Expect(picker.Locator(".media-tile").First).ToBeVisibleAsync();
        await picker.Locator("[data-media-upload] input[type=file]").SetInputFilesAsync(
            new FilePayload { Name = $"{mark}.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Png) });
        await Expect(picker.Locator(".media-upload-done")).ToHaveCountAsync(1);
        await Tile(picker, $"{mark}.png").ClickAsync();
        await Expect(picker).ToBeHiddenAsync();
        await Expect(Names(tab)).ToHaveTextAsync([$"{mark}.png"]);
        await Expect(tab.Page.Locator("[data-field='hero'] .media-field-thumb img")).Not.ToHaveJSPropertyAsync("naturalWidth", 0);

        // Removed, then the sample's image chosen from the library by a search.
        await tab.Page.Locator($"[data-field='hero'] fluent-button[aria-label=\"{Text("Media.Field.Remove", $"{mark}.png")}\"]").ClickAsync();
        await Expect(Names(tab)).ToHaveCountAsync(0);
        await FieldButton(tab, "Media.Field.Choose").ClickAsync();
        await SearchAsync(picker, "welcome");
        await Tile(picker, Welcome).ClickAsync();
        await Expect(Names(tab)).ToHaveTextAsync([Welcome]);

        var saved = await tab.Page.RunAndWaitForResponseAsync(
            () => tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Form.Save"))}\s*$") }).First.ClickAsync(),
            response => response.Request.Method == "POST" && response.Url == tab.ManagementUrl("content/article"),
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        saved.Status.ShouldBe(201);
        var hero = Guid.Parse(JsonNode.Parse(saved.Request.PostData!)!["hero"]!.GetValue<string>());

        // The list reads the field populated, in its one read, and shows the image.
        var list = await AnswerAsync(tab, () => tab.Page.GotoAsync(new Uri(Host.Admin, "content/article?columns=hero").AbsoluteUri), response => response.Url.StartsWith(tab.ManagementUrl("content/article") + "?", StringComparison.Ordinal));
        list.Url.ShouldContain("populate=hero");
        var row = tab.Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = mark });
        await Expect(row.Locator(".media-cell-name")).ToHaveTextAsync(Welcome);
        await Expect(row.Locator(".media-cell img")).ToHaveAttributeAsync("src", new Regex($"/media/{hero:D}/original/"));
        await Expect(row.Locator(".media-cell img")).Not.ToHaveJSPropertyAsync("naturalWidth", 0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThePickerAndTheFieldWorkByKeyboardAloneAndTheAltTextIsSavedOnTheAsset()
    {
        var mark = Mark();
        var browser = await OpenBrowserAsync();
        var tab = await NewArticleAsync(browser, Host.Editor, mark);
        var keyboard = tab.Page.Keyboard;
        var choose = FieldButton(tab, "Media.Field.Choose");

        // Escape closes the picker without a choice and gives focus back.
        await PressAsync(choose, "Enter");
        var picker = Picker(tab);
        await Expect(picker.Locator(".media-tile").First).ToBeVisibleAsync();
        await keyboard.PressAsync("Escape");
        await Expect(picker).ToBeHiddenAsync();
        await FocusReturnedAsync(choose);
        await Expect(Names(tab)).ToHaveCountAsync(0);

        // The search by keys and its button, then Enter on a tile chooses it.
        await PressAsync(choose, "Enter");
        var search = picker.GetByLabel(Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true });
        await PressAsync(search, "Control+a");
        await keyboard.TypeAsync("welcome");
        await Expect(search).ToHaveValueAsync("welcome");
        await PressAsync(picker.Locator("fluent-button").Filter(Exactly("Media.Search.Submit")), "Enter");
        await Expect(picker.Locator(".media-tile-name")).ToHaveTextAsync([Welcome]);
        await PressAsync(Tile(picker, Welcome), "Enter");
        await Expect(picker).ToBeHiddenAsync();
        await Expect(Names(tab)).ToHaveTextAsync([Welcome]);
        await FocusReturnedAsync(choose);

        // The alt text opens under the asset by Enter and is saved on the asset as focus leaves it.
        var details = tab.Page.Locator("[data-field='hero'] .media-field-toggle");
        await PressAsync(details, "Enter");
        await Expect(details).ToHaveAttributeAsync("aria-expanded", "true");
        var alt = tab.Page.Locator("[data-field='hero']").GetByLabel(Text("Media.Alt.Label"), new LocatorGetByLabelOptions { Exact = true });
        await PressAsync(alt, "Control+a");
        await keyboard.TypeAsync($"{mark} banner");
        var patched = await AnswerAsync(tab, () => keyboard.PressAsync("Tab"), response => response.Request.Method == "PATCH" && response.Url.Contains("/media/", StringComparison.Ordinal));
        patched.Status.ShouldBe(200);
        JsonNode.Parse(patched.Request.PostData!)!["alt"]!.GetValue<string>().ShouldBe($"{mark} banner");

        // Removed by Enter on its button; focus goes to the picker's button.
        await PressAsync(tab.Page.Locator($"[data-field='hero'] fluent-button[aria-label=\"{Text("Media.Field.Remove", Welcome)}\"]"), "Enter");
        await Expect(Names(tab)).ToHaveCountAsync(0);
        await Expect(choose).ToBeFocusedAsync();
        await Expect(tab.Announced(Text("Media.Field.Removed", Welcome))).ToBeAttachedAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private async Task<AdminTab> NewArticleAsync(AdminBrowser browser, Editor user, string mark)
    {
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, user);
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"{mark} with a hero");
        await Expect(FieldButton(tab, "Media.Field.Choose")).ToBeVisibleAsync();
        return tab;
    }

    private static async Task<IResponse> AnswerAsync(AdminTab tab, Func<Task> action, Func<IResponse, bool> answer) =>
        await tab.Page.RunAndWaitForResponseAsync(action, answer, new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });

    private static async Task SearchAsync(ILocator picker, string text)
    {
        await picker.GetByLabel(Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true }).FillAsync(text);
        await picker.Locator("fluent-button").Filter(Exactly("Media.Search.Submit")).ClickAsync();
    }

    // A dialog gives focus back to its opener as it closes, and the component library does it again 25 ms later.
    private static async Task FocusReturnedAsync(ILocator opener)
    {
        await Expect(opener).ToBeFocusedAsync();
        await opener.Page.WaitForTimeoutAsync(150);
    }

    // Focus as Tab would give it, then a key: no pointer is used.
    private static async Task PressAsync(ILocator target, string key)
    {
        await target.FocusAsync();
        await Expect(target).ToBeFocusedAsync();
        await target.Page.Keyboard.PressAsync(key);
    }

    private static string Mark() => "f" + Guid.NewGuid().ToString("N")[..8];

    private static string Text(string key, params string[] arguments)
    {
        var text = Repository.MediaText(key);
        for (var index = 0; index < arguments.Length; index++)
        {
            text = text.Replace($"{{{index}}}", arguments[index], StringComparison.Ordinal);
        }

        return text;
    }

    private static LocatorFilterOptions Exactly(string key) => new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Text(key))}\s*$") };

    private static ILocator FieldButton(AdminTab tab, string key) =>
        tab.Page.Locator("[data-field='hero'] .media-field-actions fluent-button").Filter(Exactly(key));

    private static ILocator Names(AdminTab tab) => tab.Page.Locator("[data-field='hero'] .media-field-name");

    private static ILocator Picker(AdminTab tab) => tab.Page.Locator($"fluent-dialog[aria-label=\"{Text("Media.Picker.TitleOne")}\"]");

    private static ILocator Tile(ILocator picker, string name) =>
        picker.Locator(".media-tile-open").Filter(new LocatorFilterOptions { Has = picker.Page.Locator(".media-tile-name", new PageLocatorOptions { HasTextString = name }) });
}
