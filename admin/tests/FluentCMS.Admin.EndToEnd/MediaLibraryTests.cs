using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The media library of A-21 on the reference host: browse, upload by button, by drop and by paste with a state per file,
// a folder round trip, and assets moved to another folder. The host is shared, so every name a case makes is its own.
public sealed class MediaLibraryTests(EndToEndHost host) : BrowserTest(host)
{
    // A PNG of one pixel.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    // The start of a Windows program, which the server never stores (FCMS1583).
    private static readonly byte[] Program = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

    [Fact]
    public async Task TheEditorUploadsByButtonSearchesFiltersAndSwitchesToTheList()
    {
        var mark = Mark();
        var browser = await OpenBrowserAsync();
        var tab = await OpenLibraryAsync(browser, Host.Editor);

        await UploadInput(tab).SetInputFilesAsync(
        [
            new FilePayload { Name = $"{mark}-photo.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Png) },
            new FilePayload { Name = $"{mark}-setup.exe", MimeType = "application/octet-stream", Buffer = Program },
        ]);

        await Expect(UploadItem(tab, $"{mark}-photo.png")).ToContainTextAsync(Text("Media.Upload.State.Done"));
        await Expect(UploadItem(tab, $"{mark}-setup.exe")).ToContainTextAsync(Text("Media.Upload.Refused.FCMS1583"));
        // A refusal is announced in the assertive region.
        await Expect(tab.Page.GetByRole(AriaRole.Alert).GetByText(Text("Media.Upload.FinishedWithRefused", "1", "1"), new LocatorGetByTextOptions { Exact = true }).Last).ToBeAttachedAsync();

        // The search is in the address and finds the upload; the type filter keeps it.
        await Content(tab).GetByLabel(Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true }).FillAsync(mark);
        await MediaButton(tab, "Media.Search.Submit").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"q={mark}"));
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-photo.png"]);
        await Content(tab).GetByLabel(Text("Media.Type.Label"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync("image/*");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("type=image"));
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-photo.png"]);

        await MediaButton(tab, "Media.View.List").ClickAsync();
        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = Text("Media.Results.Label"), Exact = true });
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(grid.Locator("tbody tr")).ToContainTextAsync("image/png");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("view=list"));

        // The editor may not manage folders, so none of their actions shows.
        await Expect(MediaButton(tab, "Media.Folder.New")).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task TenFilesDroppedUploadWithAStateEachAPastedImageJoinsThemAndAnAssetMovesToAnotherFolder()
    {
        var mark = Mark();
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await OpenLibraryAsync(browser, admin);

        var target = await CreateFolderAsync(tab, $"{mark}-target");
        var drops = await CreateFolderAsync(tab, $"{mark}-drops");
        await OpenFolderAsync(tab, drops);

        string[] names = [.. Enumerable.Range(1, 9).Select(index => $"{mark}-{index}.png"), $"{mark}-setup.exe"];
        await DropAsync(tab, names);

        await Expect(tab.Page.Locator(".media-upload-done")).ToHaveCountAsync(9);
        await Expect(UploadItem(tab, $"{mark}-setup.exe")).ToContainTextAsync(Text("Media.Upload.Refused.FCMS1583"));
        await Expect(Tiles(tab)).ToHaveCountAsync(9);

        // A pasted image goes into the open folder too.
        await tab.Page.EvaluateAsync(
            """
            ([name, png]) => {
                const bytes = Uint8Array.from(atob(png), c => c.charCodeAt(0));
                const transfer = new DataTransfer();
                transfer.items.add(new File([bytes], name, { type: "image/png" }));
                document.body.dispatchEvent(new ClipboardEvent("paste", { bubbles: true, cancelable: true, clipboardData: transfer }));
            }
            """,
            new object[] { $"{mark}-pasted.png", Png });
        await Expect(Tiles(tab)).ToHaveCountAsync(10);
        await Expect(Tiles(tab).Filter(new LocatorFilterOptions { HasText = $"{mark}-pasted.png" })).ToHaveCountAsync(1);

        // One asset moves to the other folder, by its checkbox and the move dialog.
        await Content(tab).GetByLabel(Text("Media.Asset.Select", $"{mark}-1.png"), new LocatorGetByLabelOptions { Exact = true }).CheckAsync();
        await MediaButton(tab, "Media.Move.Assets", "1").ClickAsync();
        await tab.Page.GetByLabel(Text("Media.Move.Target"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Value = target.ToString("D") });
        await MediaButton(tab, "Media.Move.Save").ClickAsync();
        await Expect(Tiles(tab)).ToHaveCountAsync(9);
        await Expect(tab.Announced(Text("Media.Move.Moved", "1"))).ToBeAttachedAsync();

        await OpenFolderAsync(tab, target);
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-1.png"]);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFolderIsCreatedRenamedMovedAndDeletedOnceEmptyAndANonEmptyOneSaysWhyNot()
    {
        var mark = Mark();
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await OpenLibraryAsync(browser, admin);

        var parent = await CreateFolderAsync(tab, $"{mark}-parent");
        var trip = await CreateFolderAsync(tab, $"{mark}-trip");
        await OpenFolderAsync(tab, trip);

        await MediaButton(tab, "Media.Folder.Rename").ClickAsync();
        var name = tab.Page.GetByLabel(Text("Media.Folder.Name"));
        await Expect(name).ToHaveValueAsync($"{mark}-trip");
        await name.FillAsync($"{mark}-renamed");
        await MediaButton(tab, "Media.Folder.RenameSave").ClickAsync();
        await Expect(TreeItem(tab, trip)).ToContainTextAsync($"{mark}-renamed");

        await MediaButton(tab, "Media.Folder.Move").ClickAsync();
        await tab.Page.GetByLabel(Text("Media.Move.Target"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Value = parent.ToString("D") });
        await MediaButton(tab, "Media.Move.Save").ClickAsync();
        await Expect(TreeItem(tab, parent).Locator($"fluent-tree-item[id='{trip:D}']")).ToHaveCountAsync(1);

        // With a file in it, the folder is not deleted, and the dialog says why.
        await UploadInput(tab).SetInputFilesAsync(new FilePayload { Name = $"{mark}-kept.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Png) });
        await Expect(Tiles(tab)).ToHaveCountAsync(1);
        await MediaButton(tab, "Media.Folder.Delete").ClickAsync();
        await MediaButton(tab, "Media.Folder.DeleteConfirm").ClickAsync();
        await Expect(tab.Page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = Text("Media.Folder.NotEmpty") })).ToBeVisibleAsync();
        await MediaButton(tab, "Media.Dialog.Cancel").ClickAsync();

        // Emptied, it goes, and the library opens its parent.
        await Content(tab).GetByLabel(Text("Media.Asset.Select", $"{mark}-kept.png"), new LocatorGetByLabelOptions { Exact = true }).CheckAsync();
        await MediaButton(tab, "Media.Move.Assets", "1").ClickAsync();
        await tab.Page.GetByLabel(Text("Media.Move.Target"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Value = parent.ToString("D") });
        await MediaButton(tab, "Media.Move.Save").ClickAsync();
        await Expect(Tiles(tab)).ToHaveCountAsync(0);
        await MediaButton(tab, "Media.Folder.Delete").ClickAsync();
        await MediaButton(tab, "Media.Folder.DeleteConfirm").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"folder={parent:D}"));
        await Expect(TreeItem(tab, trip)).ToHaveCountAsync(0);
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-kept.png"]);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFolderRoundTripAndAnUploadByTheButtonWorkByKeyboardAlone()
    {
        var mark = Mark();
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await OpenLibraryAsync(browser, admin);
        var keyboard = tab.Page.Keyboard;

        // A folder is created: the button by Enter, the name typed, the dialog submitted by Enter.
        await PressAsync(MediaButton(tab, "Media.Folder.New"), "Enter");
        await PressAsync(tab.Page.GetByLabel(Text("Media.Folder.Name")), "Control+a");
        await keyboard.TypeAsync($"{mark}-keys");
        var created = await tab.Page.RunAndWaitForResponseAsync(
            () => keyboard.PressAsync("Enter"),
            response => response.Url == tab.ManagementUrl("media/folders") && response.Request.Method == "POST",
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        var folder = Guid.Parse(JsonNode.Parse(await created.TextAsync())!["id"]!.GetValue<string>());
        await Expect(TreeItem(tab, folder)).ToHaveCountAsync(1);
        await FocusReturnedAsync(MediaButton(tab, "Media.Folder.New"));

        // Opened from the tree by Enter on its item.
        await PressAsync(TreeItem(tab, folder), "Enter");
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"folder={folder:D}"));

        // The upload's button opens the file chooser by Space.
        var chooser = await tab.Page.RunAndWaitForFileChooserAsync(() => PressAsync(UploadInput(tab), "Space"));
        await chooser.SetFilesAsync(new FilePayload { Name = $"{mark}-keys.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Png) });
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-keys.png"]);

        // The tile opens its detail by Enter, and Escape closes it.
        await PressAsync(Content(tab).Locator(".media-tile-open"), "Enter");
        await Expect(tab.Page.Locator(".media-panel-facts")).ToBeVisibleAsync();
        await keyboard.PressAsync("Escape");
        await Expect(tab.Page.Locator(".media-panel-facts")).ToBeHiddenAsync();
        await FocusReturnedAsync(Content(tab).Locator(".media-tile-open"));

        // Renamed by keyboard.
        await PressAsync(MediaButton(tab, "Media.Folder.Rename"), "Enter");
        await PressAsync(tab.Page.GetByLabel(Text("Media.Folder.Name")), "Control+a");
        await keyboard.TypeAsync($"{mark}-renamed");
        await keyboard.PressAsync("Enter");
        await Expect(TreeItem(tab, folder)).ToContainTextAsync($"{mark}-renamed");
        await FocusReturnedAsync(MediaButton(tab, "Media.Folder.Rename"));

        // Not deleted while it holds the file; Escape closes the dialog.
        await PressAsync(MediaButton(tab, "Media.Folder.Delete"), "Enter");
        await PressAsync(MediaButton(tab, "Media.Folder.DeleteConfirm"), "Enter");
        await Expect(tab.Page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = Text("Media.Folder.NotEmpty") })).ToBeVisibleAsync();
        await keyboard.PressAsync("Escape");
        await FocusReturnedAsync(MediaButton(tab, "Media.Folder.Delete"));

        // The file is selected by Space and moved to the root, and the empty folder goes.
        await PressAsync(Content(tab).GetByLabel(Text("Media.Asset.Select", $"{mark}-keys.png"), new LocatorGetByLabelOptions { Exact = true }), "Space");
        await PressAsync(MediaButton(tab, "Media.Move.Assets", "1"), "Enter");
        await tab.Page.GetByLabel(Text("Media.Move.Target"), new PageGetByLabelOptions { Exact = true }).FocusAsync();
        await keyboard.PressAsync("ArrowDown");
        await Expect(tab.Page.GetByLabel(Text("Media.Move.Target"), new PageGetByLabelOptions { Exact = true })).ToHaveValueAsync("root");
        await PressAsync(MediaButton(tab, "Media.Move.Save"), "Enter");
        await Expect(Tiles(tab)).ToHaveCountAsync(0);
        await PressAsync(MediaButton(tab, "Media.Folder.Delete"), "Enter");
        await PressAsync(MediaButton(tab, "Media.Folder.DeleteConfirm"), "Enter");
        await Expect(tab.Page).Not.ToHaveURLAsync(new Regex($"folder={folder:D}"));
        await Expect(TreeItem(tab, folder)).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A dialog of the library gives focus back to its opener on a timer once it has closed; the next key waits for it.
    private static Task FocusReturnedAsync(ILocator opener) => Expect(opener).ToBeFocusedAsync();

    // Focus as Tab would give it, then a key: no pointer is used.
    private static async Task PressAsync(ILocator target, string key)
    {
        await target.FocusAsync();
        await target.Page.Keyboard.PressAsync(key);
    }

    private static string Mark() => "m" + Guid.NewGuid().ToString("N")[..8];

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

    private static ILocator UploadInput(AdminTab tab) => tab.Page.Locator("[data-media-upload] input[type=file]");

    private static ILocator UploadItem(AdminTab tab, string name) =>
        tab.Page.Locator(".media-upload-item").Filter(new LocatorFilterOptions { Has = tab.Page.Locator(".media-upload-name", new PageLocatorOptions { HasTextString = name }) });

    private static ILocator Tiles(AdminTab tab) => tab.Page.Locator(".media-tile-name");

    private static ILocator TreeItem(AdminTab tab, Guid folder) => tab.Page.Locator($"fluent-tree-item[id='{folder:D}']");

    // Creates a folder in the open one and answers its id, from the server's answer.
    private static async Task<Guid> CreateFolderAsync(AdminTab tab, string name)
    {
        await MediaButton(tab, "Media.Folder.New").ClickAsync();
        await tab.Page.GetByLabel(Text("Media.Folder.Name")).FillAsync(name);
        var created = await tab.Page.RunAndWaitForResponseAsync(
            () => MediaButton(tab, "Media.Folder.Create").ClickAsync(),
            response => response.Url == tab.ManagementUrl("media/folders") && response.Request.Method == "POST",
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        created.Status.ShouldBe(201);
        var id = Guid.Parse(JsonNode.Parse(await created.TextAsync())!["id"]!.GetValue<string>());
        await Expect(TreeItem(tab, id)).ToHaveCountAsync(1);
        return id;
    }

    // Opens a folder from the tree, as a user does.
    private static async Task OpenFolderAsync(AdminTab tab, Guid folder)
    {
        await TreeItem(tab, folder).ClickAsync(new LocatorClickOptions { Position = new Position { X = 40, Y = 12 } });
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"folder={folder:D}"));
    }

    // Drops the files on the page's heading, which is no drop target of its own, so that the library's zone takes them.
    private static async Task DropAsync(AdminTab tab, string[] names) =>
        await tab.Page.EvaluateAsync(
            """
            ([names, png]) => {
                const bytes = Uint8Array.from(atob(png), c => c.charCodeAt(0));
                const program = new Uint8Array([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
                const transfer = new DataTransfer();
                for (const name of names) {
                    transfer.items.add(new File([name.endsWith(".exe") ? program : bytes], name));
                }

                const target = document.querySelector("main h1");
                for (const type of ["dragenter", "dragover", "drop"]) {
                    target.dispatchEvent(new DragEvent(type, { bubbles: true, cancelable: true, dataTransfer: transfer }));
                }
            }
            """,
            new object[] { names, Png });
}
