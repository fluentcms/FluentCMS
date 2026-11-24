using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.EndToEnd.MediaUi;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The media library of A-21 on the reference host: browse, upload by button, by drop and by paste with a state per file,
// a folder round trip, and assets moved to another folder. The host is shared, so every name a case makes is its own.
public sealed class MediaLibraryTests(EndToEndHost host) : BrowserTest(host)
{
    // The start of a Windows program, which the server never stores (FCMS1583).
    private static readonly byte[] Program = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

    [Fact]
    public async Task TheEditorUploadsByButtonSearchesFiltersAndSwitchesToTheList()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var tab = await OpenLibraryAsync(browser);

        await UploadAsync(tab.Page.Locator("main"), 1, PngFile($"{mark}-photo.png"), new FilePayload { Name = $"{mark}-setup.exe", MimeType = "application/octet-stream", Buffer = Program });

        await Expect(UploadItem(tab, $"{mark}-photo.png")).ToContainTextAsync(Text("Media.Upload.State.Done"));
        await Expect(UploadItem(tab, $"{mark}-setup.exe")).ToContainTextAsync(Text("Media.Upload.Refused.FCMS1583"));
        // A refusal is announced in the assertive region.
        await Expect(tab.Page.GetByRole(AriaRole.Alert).GetByText(Text("Media.Upload.FinishedWithRefused", "1", "1"), new LocatorGetByTextOptions { Exact = true }).Last).ToBeAttachedAsync();

        // The search is in the address and finds the upload; the type filter keeps it.
        await SearchAsync(Content(tab), mark);
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"q={mark}"));
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-photo.png"]);
        await Content(tab).GetByLabel(Text("Media.Type.Label"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync("image/*");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("type=image"));
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-photo.png"]);

        await Button(tab, "Media.View.List").ClickAsync();
        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = Text("Media.Results.Label"), Exact = true });
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(grid.Locator("tbody tr")).ToContainTextAsync("image/png");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("view=list"));

        // The editor may not manage folders, so none of their actions shows.
        await Expect(Button(tab, "Media.Folder.New")).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task TenFilesDroppedUploadWithAStateEachAPastedImageJoinsThemAndAnAssetMovesToAnotherFolder()
    {
        var mark = Mark;
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
                const paste = new ClipboardEvent("paste", { bubbles: true, cancelable: true, clipboardData: transfer });
                // Firefox gives an event made by a script an empty clipboard of its own, whatever the event was made with.
                Object.defineProperty(paste, "clipboardData", { value: transfer });
                document.body.dispatchEvent(paste);
            }
            """,
            new object[] { $"{mark}-pasted.png", Png });
        await Expect(Tiles(tab)).ToHaveCountAsync(10);
        await Expect(Tiles(tab).Filter(new LocatorFilterOptions { HasText = $"{mark}-pasted.png" })).ToHaveCountAsync(1);

        // One asset moves to the other folder, by its checkbox and the move dialog.
        await Content(tab).GetByLabel(Text("Media.Asset.Select", $"{mark}-1.png"), new LocatorGetByLabelOptions { Exact = true }).CheckAsync();
        await MoveAsync(tab, () => Button(tab, "Media.Move.Assets", "1").ClickAsync(), "Media.Move.AssetsTitle", target);
        await Expect(Tiles(tab)).ToHaveCountAsync(9);
        await Expect(tab.Announced(Text("Media.Move.Moved", "1"))).ToBeAttachedAsync();

        await OpenFolderAsync(tab, target);
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-1.png"]);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFolderIsCreatedRenamedMovedAndDeletedOnceEmptyAndANonEmptyOneSaysWhyNot()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await OpenLibraryAsync(browser, admin);

        var parent = await CreateFolderAsync(tab, $"{mark}-parent");
        var trip = await CreateFolderAsync(tab, $"{mark}-trip");
        await OpenFolderAsync(tab, trip);

        // The create dialog's input is the same element, still holding the name, so its value says nothing until this is shown.
        var rename = await tab.OpenDialogAsync(() => Button(tab, "Media.Folder.Rename").ClickAsync(), Text("Media.Folder.RenameTitle"));
        var name = rename.GetByLabel(Text("Media.Folder.Name"));
        await Expect(name).ToHaveValueAsync($"{mark}-trip");
        await name.FillAsync($"{mark}-renamed");
        var renamed = await tab.AnswerAsync(() => Button(tab, "Media.Folder.RenameSave").ClickAsync(), "PATCH", tab.ManagementUrl($"media/folders/{trip:D}"));
        JsonNode.Parse(renamed.Request.PostData!)!["name"]!.GetValue<string>().ShouldBe($"{mark}-renamed");
        renamed.Status.ShouldBe(200);
        await Expect(TreeItem(tab, trip)).ToContainTextAsync($"{mark}-renamed");

        await MoveAsync(tab, () => Button(tab, "Media.Folder.Move").ClickAsync(), "Media.Folder.MoveTitle", parent);
        await Expect(TreeItem(tab, parent).Locator($"fluent-tree-item[id='{trip:D}']")).ToHaveCountAsync(1);

        // With a file in it, the folder is not deleted, and the dialog says why.
        await UploadAsync(tab.Page.Locator("main"), PngFile($"{mark}-kept.png"));
        await Expect(Tiles(tab)).ToHaveCountAsync(1);
        await tab.OpenDialogAsync(() => Button(tab, "Media.Folder.Delete").ClickAsync(), Text("Media.Folder.DeleteTitle"));
        await Button(tab, "Media.Folder.DeleteConfirm").ClickAsync();
        await Expect(tab.Page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = Text("Media.Folder.NotEmpty") })).ToBeVisibleAsync();
        await Button(tab, "Media.Dialog.Cancel").ClickAsync();

        // Emptied, it goes, and the library opens its parent.
        await Content(tab).GetByLabel(Text("Media.Asset.Select", $"{mark}-kept.png"), new LocatorGetByLabelOptions { Exact = true }).CheckAsync();
        await MoveAsync(tab, () => Button(tab, "Media.Move.Assets", "1").ClickAsync(), "Media.Move.AssetsTitle", parent);
        await Expect(Tiles(tab)).ToHaveCountAsync(0);
        await tab.OpenDialogAsync(() => Button(tab, "Media.Folder.Delete").ClickAsync(), Text("Media.Folder.DeleteTitle"));
        await Button(tab, "Media.Folder.DeleteConfirm").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"folder={parent:D}"));
        await Expect(TreeItem(tab, trip)).ToHaveCountAsync(0);
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-kept.png"]);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFolderRoundTripAndAnUploadByTheButtonWorkByKeyboardAlone()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var admin = await Host.CreateEditorAsync("Super Admin");
        var tab = await OpenLibraryAsync(browser, admin);
        var keyboard = tab.Page.Keyboard;

        // A folder is created: the button by Enter, the name typed, the dialog submitted by Enter.
        await Ui.PressAsync(Button(tab, "Media.Folder.New"), "Enter");
        await Expect(tab.ShownDialog(Text("Media.Folder.NewTitle"))).ToBeVisibleAsync();
        await Ui.PressAsync(tab.Page.GetByLabel(Text("Media.Folder.Name")), "Control+a");
        await keyboard.TypeAsync($"{mark}-keys");
        var folder = await Ui.CreatedIdAsync(await tab.AnswerAsync(() => keyboard.PressAsync("Enter"), "POST", tab.ManagementUrl("media/folders")), "media/folders");
        await Expect(TreeItem(tab, folder)).ToHaveCountAsync(1);
        await Ui.FocusReturnedAsync(Button(tab, "Media.Folder.New"));

        // Opened from the tree by Enter on its item, once the tree's focus group has made it focusable.
        await Expect(TreeItem(tab, folder)).ToHaveAttributeAsync("tabindex", new Regex("^-?[0-9]+$"));
        await Ui.PressAsync(TreeItem(tab, folder), "Enter");
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"folder={folder:D}"));

        // The upload's button opens the file chooser by Space.
        var chooser = await tab.Page.RunAndWaitForFileChooserAsync(() => Ui.PressAsync(UploadInput(tab.Page.Locator("main")), "Space"));
        await chooser.SetFilesAsync(PngFile($"{mark}-keys.png"));
        await Expect(Tiles(tab)).ToHaveTextAsync([$"{mark}-keys.png"]);

        // The tile opens its detail by Enter, and Escape closes it.
        await Ui.PressAsync(Content(tab).Locator(".media-tile-open"), "Enter");
        await Expect(tab.Page.Locator(".media-panel-facts")).ToBeVisibleAsync();
        await keyboard.PressAsync("Escape");
        await Expect(tab.Page.Locator(".media-panel-facts")).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(Content(tab).Locator(".media-tile-open"));

        // Renamed by keyboard.
        await Ui.PressAsync(Button(tab, "Media.Folder.Rename"), "Enter");
        await Expect(tab.ShownDialog(Text("Media.Folder.RenameTitle"))).ToBeVisibleAsync();
        await Ui.PressAsync(tab.Page.GetByLabel(Text("Media.Folder.Name")), "Control+a");
        await keyboard.TypeAsync($"{mark}-renamed");
        await keyboard.PressAsync("Enter");
        await Expect(TreeItem(tab, folder)).ToContainTextAsync($"{mark}-renamed");
        await Ui.FocusReturnedAsync(Button(tab, "Media.Folder.Rename"));

        // Not deleted while it holds the file; Escape closes the dialog.
        await Ui.PressAsync(Button(tab, "Media.Folder.Delete"), "Enter");
        await Ui.PressAsync(Button(tab, "Media.Folder.DeleteConfirm"), "Enter");
        await Expect(tab.Page.GetByRole(AriaRole.Alert).Filter(new LocatorFilterOptions { HasText = Text("Media.Folder.NotEmpty") })).ToBeVisibleAsync();
        await keyboard.PressAsync("Escape");
        await Ui.FocusReturnedAsync(Button(tab, "Media.Folder.Delete"));

        // The file is selected by Space and moved to the root, and the empty folder goes.
        var check = Content(tab).GetByLabel(Text("Media.Asset.Select", $"{mark}-keys.png"), new LocatorGetByLabelOptions { Exact = true });
        await Ui.PressAsync(check, "Space");
        await Expect(check).ToBeCheckedAsync();
        await Ui.PressAsync(Button(tab, "Media.Move.Assets", "1"), "Enter");
        var target = tab.Page.GetByLabel(Text("Media.Move.Target"), new PageGetByLabelOptions { Exact = true });
        await Ui.PressAsync(target, "ArrowDown");
        await Expect(target).ToHaveValueAsync("root");
        await Ui.PressAsync(Button(tab, "Media.Move.Save"), "Enter");
        await Expect(Tiles(tab)).ToHaveCountAsync(0);
        await Ui.PressAsync(Button(tab, "Media.Folder.Delete"), "Enter");
        await Ui.PressAsync(Button(tab, "Media.Folder.DeleteConfirm"), "Enter");
        await Expect(tab.Page).Not.ToHaveURLAsync(new Regex($"folder={folder:D}"));
        await Expect(TreeItem(tab, folder)).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private async Task<AdminTab> OpenLibraryAsync(AdminBrowser browser, Editor? user = null)
    {
        var tab = await SignedInTabAsync(browser, "media", user);
        await Expect(Content(tab).GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Text("Media.Page.Heading"), Exact = true })).ToBeVisibleAsync();
        return tab;
    }

    private static ILocator Content(AdminTab tab) => tab.Page.Locator("main");

    private static ILocator UploadItem(AdminTab tab, string name) =>
        tab.Page.Locator(".media-upload-item").Filter(new LocatorFilterOptions { Has = tab.Page.Locator(".media-upload-name", new PageLocatorOptions { HasTextString = name }) });

    private static ILocator Tiles(AdminTab tab) => tab.Page.Locator(".media-tile-name");

    private static ILocator TreeItem(AdminTab tab, Guid folder) => tab.Page.Locator($"fluent-tree-item[id='{folder:D}']");

    // Creates a folder in the open one and answers its id, from the Location of the server's answer.
    private static async Task<Guid> CreateFolderAsync(AdminTab tab, string name)
    {
        var dialog = await tab.OpenDialogAsync(() => Button(tab, "Media.Folder.New").ClickAsync(), Text("Media.Folder.NewTitle"));
        await dialog.GetByLabel(Text("Media.Folder.Name")).FillAsync(name);
        var created = await tab.AnswerAsync(() => Button(tab, "Media.Folder.Create").ClickAsync(), "POST", tab.ManagementUrl("media/folders"));
        var id = await Ui.CreatedIdAsync(created, "media/folders");
        await Expect(TreeItem(tab, id)).ToHaveCountAsync(1);
        return id;
    }

    // Moves the selection or the open folder to a folder through the move dialog that the action opens.
    private static async Task MoveAsync(AdminTab tab, Func<Task> open, string titleKey, Guid folder)
    {
        var dialog = await tab.OpenDialogAsync(open, Text(titleKey));
        await dialog.GetByLabel(Text("Media.Move.Target"), new LocatorGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Value = folder.ToString("D") });
        await Button(tab, "Media.Move.Save").ClickAsync();
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
