using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.EndToEnd.MediaUi;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The media field, its list cell and the media picker of A-20 on the Blog sample's Article.Hero. The host is shared, so a
// case works only on assets it uploads itself, under names with a mark of its own, and finds them by a search.
public sealed class MediaFieldTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorPicksFromTheLibraryUploadsAnotherRemovesItSavesAndTheListShowsTheImage()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var tab = await NewArticleAsync(browser, mark);
        var choose = FieldButton(tab, "Media.Field.Choose");
        var picker = Picker(tab);

        // An image of the library, put there by this case.
        await OpenPickerAsync(tab, () => choose.ClickAsync());
        await UploadAsync(picker, PngFile($"{mark}-library.png"));
        await DialogButton(picker, "Media.Dialog.Cancel").ClickAsync();
        await Expect(picker).ToBeHiddenAsync();

        // Picked from the library by a search.
        await OpenPickerAsync(tab, () => choose.ClickAsync());
        await SearchAsync(picker, mark);
        await Tile(picker, $"{mark}-library.png").ClickAsync();
        await Expect(picker).ToBeHiddenAsync();
        await Expect(Names(tab)).ToHaveTextAsync([$"{mark}-library.png"]);
        await Expect(tab.Page.Locator("[data-field='hero'] .media-field-thumb img")).Not.ToHaveJSPropertyAsync("naturalWidth", 0);

        // Another uploaded from the picker and chosen, then removed.
        await OpenPickerAsync(tab, () => choose.ClickAsync());
        await SearchAsync(picker, mark);
        await UploadAsync(picker, PngFile($"{mark}-upload.png"));
        await Tile(picker, $"{mark}-upload.png").ClickAsync();
        await Expect(Names(tab)).ToHaveTextAsync([$"{mark}-upload.png"]);
        await RemoveButton(tab, $"{mark}-upload.png").ClickAsync();
        await Expect(Names(tab)).ToHaveCountAsync(0);

        // The library's image again, and the save.
        await OpenPickerAsync(tab, () => choose.ClickAsync());
        await SearchAsync(picker, $"{mark}-library");
        await Tile(picker, $"{mark}-library.png").ClickAsync();
        await Expect(Names(tab)).ToHaveTextAsync([$"{mark}-library.png"]);
        var saved = await tab.AnswerAsync(() => tab.Page.Locator(".content-form-toolbar fluent-button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Repository.AdminText("Content.Form.Save"))}\s*$") }).First.ClickAsync(),
            response => response.Request.Method == "POST" && response.Url == tab.ManagementUrl("content/article"));
        saved.Status.ShouldBe(201);
        var hero = Guid.Parse(JsonNode.Parse(saved.Request.PostData!)!["hero"]!.GetValue<string>());

        // The list reads the field populated, in its one read, and shows the image.
        var list = await tab.AnswerAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, "content/article?columns=hero").AbsoluteUri), response => response.Url.StartsWith(tab.ManagementUrl("content/article") + "?", StringComparison.Ordinal));
        list.Url.ShouldContain("populate=hero");
        var row = tab.Page.Locator("tr").Filter(new LocatorFilterOptions { HasText = $"{mark} with a hero" });
        await Expect(row.Locator(".media-cell-name")).ToHaveTextAsync($"{mark}-library.png");
        await Expect(row.Locator(".media-cell img")).ToHaveAttributeAsync("src", new Regex($"/media/{hero:D}/original/"));
        await Expect(row.Locator(".media-cell img")).Not.ToHaveJSPropertyAsync("naturalWidth", 0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThePickerAndTheFieldWorkByKeyboardAloneAndTheAltTextIsSavedOnTheAsset()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var tab = await NewArticleAsync(browser, mark);
        var keyboard = tab.Page.Keyboard;
        var choose = FieldButton(tab, "Media.Field.Choose");
        var picker = Picker(tab);

        // The upload's button opens the file chooser by Space; Escape closes the picker without a choice.
        await OpenPickerAsync(tab, () => Ui.PressAsync(choose, "Enter"));
        var upload = UploadInput(picker);
        await Expect(upload).ToBeEnabledAsync();
        var chooser = await tab.Page.RunAndWaitForFileChooserAsync(() => Ui.PressAsync(upload, "Space"));
        await chooser.SetFilesAsync(PngFile($"{mark}-keys.png"));
        await Expect(picker.Locator(".media-upload-done")).ToHaveCountAsync(1);
        await keyboard.PressAsync("Escape");
        await Expect(picker).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(choose);
        await Expect(Names(tab)).ToHaveCountAsync(0);

        // The search by keys and its button, then Enter on the tile chooses it.
        await OpenPickerAsync(tab, () => Ui.PressAsync(choose, "Enter"));
        var search = picker.GetByLabel(Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true });
        await Ui.PressAsync(search, "Control+a");
        await keyboard.TypeAsync(mark);
        await Expect(search).ToHaveValueAsync(mark);
        await Ui.PressAsync(DialogButton(picker, "Media.Search.Submit"), "Enter");
        await Expect(picker.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark}-keys.png"]);
        await Ui.PressAsync(Tile(picker, $"{mark}-keys.png"), "Enter");
        await Expect(picker).ToBeHiddenAsync();
        await Expect(Names(tab)).ToHaveTextAsync([$"{mark}-keys.png"]);
        await Ui.FocusReturnedAsync(choose);

        // The alt text opens under the asset by Enter and is saved on the asset as focus leaves it.
        var details = tab.Page.Locator("[data-field='hero'] .media-field-toggle");
        await Ui.PressAsync(details, "Enter");
        await Expect(details).ToHaveAttributeAsync("aria-expanded", "true");
        var alt = tab.Page.Locator("[data-field='hero']").GetByLabel(Text("Media.Alt.Label"), new LocatorGetByLabelOptions { Exact = true });
        await Ui.PressAsync(alt, "Control+a");
        await keyboard.TypeAsync($"{mark} banner");
        var patched = await tab.AnswerAsync(() => keyboard.PressAsync("Tab"), response => response.Request.Method == "PATCH" && response.Url.Contains("/media/", StringComparison.Ordinal));
        patched.Status.ShouldBe(200);
        JsonNode.Parse(patched.Request.PostData!)!["alt"]!.GetValue<string>().ShouldBe($"{mark} banner");

        // Removed by Enter on its button; focus goes to the picker's button.
        await Ui.PressAsync(RemoveButton(tab, $"{mark}-keys.png"), "Enter");
        await Expect(Names(tab)).ToHaveCountAsync(0);
        await Expect(choose).ToBeFocusedAsync();
        await Expect(tab.Announced(Text("Media.Field.Removed", $"{mark}-keys.png"))).ToBeAttachedAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThePickerOfAnImagesOnlyFieldOffersNoPdfAndItsUploadRefusesOne()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();

        // A PDF and an image in the library, uploaded where no field limits the types.
        var tab = await SignedInTabAsync(browser, "media");
        await UploadAsync(tab.Page.Locator("main"), PdfFile($"{mark}-terms.pdf"), PngFile($"{mark}-photo.png"));
        await Expect(tab.Page.Locator(".media-upload-done")).ToHaveCountAsync(2);

        // Article.Hero takes image/*, so the picker reads images only and finds the image alone.
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await Expect(FieldButton(tab, "Media.Field.Choose")).ToBeVisibleAsync();
        var picker = Picker(tab);
        var read = await tab.AnswerAsync(
            () => OpenPickerAsync(tab, () => FieldButton(tab, "Media.Field.Choose").ClickAsync()),
            response => response.Request.Method == "GET" && response.Url.StartsWith(tab.ManagementUrl("media") + "?", StringComparison.Ordinal));
        read.Url.ShouldContain("type=image");
        await SearchAsync(picker, mark);
        await Expect(picker.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark}-photo.png"]);

        // The file chooser offers images, and a PDF given anyway is refused before it is sent.
        var upload = UploadInput(picker);
        await Expect(upload).ToHaveAttributeAsync("accept", "image/*");
        var sent = 0;
        tab.Page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url == tab.ManagementUrl("media"))
            {
                Interlocked.Increment(ref sent);
            }
        };
        await upload.SetInputFilesAsync(PdfFile($"{mark}-other.pdf"));
        var refused = picker.Locator(".media-upload-refused").Filter(new LocatorFilterOptions { HasText = $"{mark}-other.pdf" });
        await Expect(refused.Locator(".media-upload-reason")).ToHaveTextAsync(Text("Media.Upload.Refused.NotAccepted"));
        await SearchAsync(picker, mark);
        await Expect(picker.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark}-photo.png"]);
        Volatile.Read(ref sent).ShouldBe(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private async Task<AdminTab> NewArticleAsync(AdminBrowser browser, string mark)
    {
        var tab = await SignedInTabAsync(browser, "content/article/new");
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"{mark} with a hero");
        await Expect(FieldButton(tab, "Media.Field.Choose")).ToBeVisibleAsync();
        return tab;
    }

    private static FilePayload PdfFile(string name) => new() { Name = name, MimeType = "application/pdf", Buffer = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n") };

    private static async Task OpenPickerAsync(AdminTab tab, Func<Task> open) => await tab.OpenDialogAsync(open, Text("Media.Picker.TitleOne"));

    private static LocatorFilterOptions Exactly(string key) => Ui.Exactly(Text(key));

    private static ILocator FieldButton(AdminTab tab, string key) =>
        tab.Page.Locator("[data-field='hero'] .media-field-actions fluent-button").Filter(Exactly(key));

    private static ILocator RemoveButton(AdminTab tab, string name) =>
        tab.Page.Locator($"[data-field='hero'] fluent-button[aria-label=\"{Text("Media.Field.Remove", name)}\"]");

    private static ILocator DialogButton(ILocator dialog, string key) => dialog.Locator("fluent-button").Filter(Exactly(key));

    private static ILocator Names(AdminTab tab) => tab.Page.Locator("[data-field='hero'] .media-field-name");

    private static ILocator Picker(AdminTab tab) => tab.Page.Locator($"fluent-dialog[aria-label=\"{Text("Media.Picker.TitleOne")}\"]");

    private static ILocator Tile(ILocator picker, string name) =>
        picker.Locator(".media-tile-open").Filter(new LocatorFilterOptions { Has = picker.Page.Locator(".media-tile-name", new PageLocatorOptions { HasTextString = name }) });
}
