using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The editor's daily loop of spec 7 by the keyboard alone (spec 4.18, A-54): one sign-in, then every step by Tab, Shift+Tab,
// the arrows, Enter, Space, Escape and typing, through KeyboardOnly, which fails a step that Tab does not reach, records any
// pointer event and asks at each stop of Tab that the focused element is not hidden under a sticky part. On the way it asserts
// what 4.18 asks: the skip link, focus on the heading after a route change, focus inside a dialog and back on its opener, focus
// that stays on the save button, and no trap. The browser's address bar is the user's too: a page it opens is no pointer.
public sealed class KeyboardLoopTests(EndToEndHost host) : BrowserTest(host)
{
    private const string PreviewTemplate = EndToEndHost.PreviewOrigin + "/showcase/{id}?preview={token}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDailyLoopRunsByTheKeyboardAlone()
    {
        var mark = Mark;
        var (articles, showcase) = await MakeAsync(mark);
        var editor = await EditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var keys = await KeyboardOnly.WatchAsync(tab);
        var page = tab.Page;
        await page.RouteAsync($"{EndToEndHost.PreviewOrigin}/**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html",
            Body = "<!doctype html><html lang=\"en\"><head><title>Preview</title></head><body><main><h1>Preview</h1></main></body></html>",
        }));

        // The hosted sign-in page, by Tab and typing.
        await page.GotoAsync(Host.Admin.AbsoluteUri);
        await SignInAsync(browser, keys, editor);
        await Expect(tab.Sidebar).ToBeVisibleAsync();

        // The skip link is the first stop and moves focus to the content.
        await keys.PressOnAsync(page.Locator("a.skip-link"), "Enter");
        await Expect(page.Locator("main#main")).ToBeFocusedAsync();

        // The theme by the user menu's radio items; the choice holds for the rest of the loop.
        await ThemeAsync(keys, editor);

        // An image uploaded in the media library, reached by the sidebar.
        await GoAsync(keys, Repository.MediaText("Media.Menu"), Repository.MediaText("Media.Page.Heading"));
        var file = $"{mark}-keys.png";
        var chooser = await page.RunAndWaitForFileChooserAsync(() => keys.PressOnAsync(MediaUi.UploadInput(page.Locator("main")), "Space"));
        await chooser.SetFilesAsync(MediaUi.PngFile(file));
        await Expect(page.Locator("main .media-upload-done")).ToHaveCountAsync(1);

        // The article found by search and by a filter, then opened.
        await GoAsync(keys, "Article", "Article");
        var grid = page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        await SearchAsync(keys, grid, mark, articles.Length);
        await FilterAsync(keys, grid, $"Keys {mark} 1");
        var entry = tab.ManagementUrl($"content/article/{articles[0]:D}");
        // A row's cells are out of the tab order: its checkbox is its stop, and Enter there opens it.
        await keys.PressOnAsync(Row(grid, $"Keys {mark} 1").Locator("input[type=checkbox]"), "Enter");
        await Expect(page.Locator("main h1")).ToHaveTextAsync($"Keys {mark} 1");
        await Expect(page.Locator("main h1")).ToBeFocusedAsync();

        await EditAsync(keys, mark, file);

        // The save keeps focus on its button and is announced.
        var save = Toolbar(tab, "Content.Form.Save");
        var saved = await tab.AnswerAsync(() => keys.PressOnAsync(save, "Enter"), "PUT", entry);
        saved.Status.ShouldBe(200);
        await Expect(tab.Announced(Text("Content.Form.Saved"))).ToBeAttachedAsync();
        await Expect(save).ToBeFocusedAsync();
        var sent = JsonNode.Parse(saved.Request.PostData!)!;
        sent["category"]!.GetValue<string>().ShouldBe("opinion");
        sent["author"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
        sent["hero"].ShouldNotBeNull();
        sent["sections"]!.AsArray().Select(block => (string)block!["type"]!).ShouldBe(["gallery", "quote"]);
        sent["sections"]![0]!["columns"]!.GetValue<int>().ShouldBe(4);
        sent["body"]!.ToJsonString().ShouldContain("bold");
        // The image, put in after the text, was moved before it by the cut and the paste.
        var blocks = sent["body"]!["content"]!.AsArray().Select(block => block!.ToJsonString()).ToList();
        blocks.Count(block => block.Contains("\"type\":\"media\"", StringComparison.Ordinal)).ShouldBe(1);
        blocks.FindIndex(block => block.Contains("\"type\":\"media\"", StringComparison.Ordinal))
            .ShouldBeLessThan(blocks.FindIndex(block => block.Contains("bold", StringComparison.Ordinal)));

        // Someone else saves first: the conflict dialog takes focus, Keep mine saves, and focus is back on the save button.
        await SaveElsewhereAsync(articles[0], $"Keys {mark} 1 saved elsewhere");
        await keys.TypeIntoAsync(page.Locator("[data-field='title'] input"), $"Keys {mark} 1 kept");
        (await tab.AnswerAsync(() => keys.PressOnAsync(save, "Enter"), "PUT", entry)).Status.ShouldBe(412);
        var conflict = tab.Dialog(Text("Content.Form.Conflict.Title"));
        await Expect(tab.ShownDialog(Text("Content.Form.Conflict.Title"))).ToBeVisibleAsync();
        await KeyboardOnly.FocusInsideAsync(conflict);
        (await tab.AnswerAsync(() => keys.PressOnAsync(Ui.ButtonIn(conflict, Text("Content.Form.Conflict.KeepMine")), "Enter"), "PUT", entry)).Status.ShouldBe(200);
        await Expect(tab.ShownDialog(Text("Content.Form.Conflict.Title"))).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(save);

        // Published, then a later publish scheduled in its dialog.
        (await tab.AnswerAsync(() => keys.PressOnAsync(Toolbar(tab, "Content.Form.Publish"), "Enter"), "POST", $"{entry}/publish")).Status.ShouldBe(200);
        await Expect(tab.Announced(Text("Content.Form.Published"))).ToBeAttachedAsync();
        await ScheduleAsync(keys, entry);

        // The preview of a showcase entry, and its width.
        await PreviewAsync(keys, showcase);

        // The articles selected by keyboard and published in bulk.
        await GoAsync(keys, "Article", "Article");
        await SearchAsync(keys, grid, mark, articles.Length);
        await BulkPublishAsync(keys, grid, articles.Length);

        // The wrapped set's dialog, drawer and toasts.
        await GalleryAsync(keys);

        // The theme chosen at the start has not flipped back; sign-out by the user menu and the hosted page.
        await Expect(page.Locator(".cms-root[data-cms-theme='dark']")).ToBeAttachedAsync();
        await SignOutAsync(keys, editor);

        keys.ShouldHaveUsedTheKeyboardAlone();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A control only a click reaches fails the walk by name, and a pointer event is seen; the mouse is used here only to show
    // that the loop above could not use it unseen.
    [Fact]
    public async Task AControlOnlyAClickReachesFailsTheWalkAndAClickIsSeen()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var keys = await KeyboardOnly.WatchAsync(tab);
        // A page of its own, served as a document, so that the watch starts with it.
        await tab.Page.RouteAsync($"{EndToEndHost.PreviewOrigin}/click-only", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html",
            Body = """
                <!doctype html>
                <html lang="en"><head><title>Click only</title></head>
                <body><main><h1>Click only</h1><button id="first">First</button><div id="clicked" onclick="this.textContent = 'Clicked'">Click me</div><button id="last">Last</button></main></body></html>
                """,
        }));
        await tab.Page.GotoAsync($"{EndToEndHost.PreviewOrigin}/click-only");

        await keys.TabToAsync(tab.Page.Locator("#last"));
        string? failure = null;
        try
        {
            await keys.TabToAsync(tab.Page.Locator("#clicked"), most: 6);
        }
        catch (ShouldAssertException exception)
        {
            failure = exception.Message;
        }

        failure.ShouldNotBeNull().ShouldContain("was not reached");
        failure.ShouldContain("button#first");
        keys.PointerEvents.ShouldBeEmpty();

        await tab.Page.Mouse.ClickAsync(1, 1);

        await Ui.UntilAsync(() => keys.PointerEvents.Count > 0, "the click was never seen");
        keys.PointerEvents.ShouldContain(seen => seen.StartsWith("pointerdown on", StringComparison.Ordinal));
    }

    // The user menu's theme items are radio items: Dark is checked by Enter, stays checked, and Escape closes the menu.
    private static async Task ThemeAsync(KeyboardOnly keys, Editor editor)
    {
        var page = keys.Tab.Page;
        var menu = UserMenu(page, editor);
        await keys.PressOnAsync(menu, "Enter");
        var dark = Theme(page, "Shell.UserMenu.ThemeDark");
        await keys.ArrowToAsync(dark);
        await Expect(Theme(page, "Shell.UserMenu.ThemeSystem")).ToHaveAttributeAsync("aria-checked", "true");
        await keys.Keys.PressAsync("Enter");
        await Expect(page.Locator(".cms-root[data-cms-theme='dark']")).ToBeAttachedAsync();
        await Expect(dark).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(Theme(page, "Shell.UserMenu.ThemeSystem")).ToHaveAttributeAsync("aria-checked", "false");
        await keys.Keys.PressAsync("Escape");
        await Ui.FocusReturnedAsync(menu);
        await Expect(page.Locator(".cms-root[data-cms-theme='dark']")).ToBeAttachedAsync();
    }

    // A page of the sidebar by its link and Enter: the route change moves focus to the page's heading.
    private static async Task GoAsync(KeyboardOnly keys, string link, string heading)
    {
        var page = keys.Tab.Page;
        await keys.PressOnAsync(keys.Tab.Sidebar.First.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = link, Exact = true }), "Enter");
        var title = page.Locator("main h1");
        await Expect(title).ToHaveTextAsync(heading);
        try
        {
            await Expect(title).ToBeFocusedAsync();
        }
        catch (PlaywrightException exception)
        {
            var focused = await page.EvaluateAsync<string>("() => document.activeElement?.outerHTML.slice(0, 200) ?? 'nothing'");
            throw new ShouldAssertException($"The route change to {link} left focus on {focused}, not on the page's heading.", exception);
        }
    }

    // The list's search by typing and Enter, sent again until the index has the case's articles.
    private static async Task SearchAsync(KeyboardOnly keys, ILocator grid, string text, int count)
    {
        var page = keys.Tab.Page;
        var search = page.GetByRole(AriaRole.Searchbox, new PageGetByRoleOptions { Name = Text("Content.Search.Label"), Exact = true });
        await keys.TypeIntoAsync(search, text);
        await Ui.UntilAsync(
            async () =>
            {
                await keys.PressOnAsync(search, "Enter");
                await Expect(page.Locator("table[aria-busy]")).ToHaveCountAsync(0);
                return await grid.Locator("tbody tr").Filter(new LocatorFilterOptions { HasText = text }).CountAsync() == count;
            },
            $"the search never found the {count} articles of {text}");
    }

    // A filter on the title by its dialog, then removed by its button.
    private static async Task FilterAsync(KeyboardOnly keys, ILocator grid, string title)
    {
        var tab = keys.Tab;
        var add = tab.Button("Content.Filter.Add");
        await keys.PressOnAsync(add, "Enter");
        var dialog = tab.Dialog(Text("Content.Filter.AddTitle"));
        await Expect(tab.ShownDialog(Text("Content.Filter.AddTitle"))).ToBeVisibleAsync();
        // A dialog opens on its first control, not on the close button of its title, so typing goes into the field.
        var field = dialog.GetByLabel(Text("Content.Filter.Field"), new LocatorGetByLabelOptions { Exact = true });
        await Expect(field).ToBeFocusedAsync();
        await keys.Keys.TypeAsync("Title");
        await keys.TabToAsync(dialog.GetByLabel(Text("Content.Filter.Operator"), new LocatorGetByLabelOptions { Exact = true }));
        await keys.Keys.TypeAsync(Text("Content.Filter.Operator.StartsWith"));
        await keys.TypeIntoAsync(dialog.GetByLabel(Text("Content.Filter.Value"), new LocatorGetByLabelOptions { Exact = true }), title);
        await keys.PressOnAsync(Ui.ButtonIn(dialog, Text("Content.Filter.Apply")), "Enter");
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"filter\[title\]\[startsWith\]="));
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(1);

        var remove = tab.Page.Locator($"fluent-button[aria-label=\"{Text("Content.Filter.Remove", $"Title starts with {title}")}\"]");
        await keys.PressOnAsync(remove, "Enter");
        await Expect(tab.Page).Not.ToHaveURLAsync(new Regex(@"filter\["));
    }

    // A text, a date and its time, a select, a relation, an image, the rich text with its toolbar, and two blocks, one with a
    // number, ordered by Alt with an arrow.
    private static async Task EditAsync(KeyboardOnly keys, string mark, string file)
    {
        var tab = keys.Tab;
        var page = tab.Page;
        await keys.TypeIntoAsync(page.Locator("[data-field='title'] input"), $"Keys {mark} 1 edited");

        var culture = CultureInfo.GetCultureInfo(await page.EvaluateAsync<string>("() => navigator.language"));
        var moment = page.Locator("[data-field='publishedOn'] input");
        await keys.TypeIntoAsync(moment.Nth(0), new DateOnly(2026, 11, 15).ToString("d", culture));
        await keys.TypeIntoAsync(moment.Nth(1), new TimeOnly(9, 30).ToString(culture.DateTimeFormat.ShortTimePattern, culture));
        await keys.Keys.PressAsync("Tab");
        await Expect(moment.Nth(0)).Not.ToHaveAttributeAsync("aria-invalid", "true");

        var category = page.Locator("[data-field='category'] select");
        await keys.TabToAsync(category);
        await keys.Keys.TypeAsync("Opinion");
        await Expect(category).ToHaveValueAsync("opinion");

        // The author from the picker: a row's checkbox by Space, Choose by Enter, and focus back on the opener.
        var chooseAuthor = page.Locator("[data-field='author'] .relation-field-actions fluent-button").Filter(Ui.Exactly(Text("Content.Relation.ChooseEntry")));
        await keys.PressOnAsync(chooseAuthor, "Enter");
        var picker = tab.Dialog("Choose an entry: Author");
        await Expect(tab.ShownDialog("Choose an entry: Author")).ToBeVisibleAsync();
        await KeyboardOnly.FocusInsideAsync(picker);
        await keys.PressOnAsync(picker.Locator(".cms-data-grid tbody tr").Filter(new LocatorFilterOptions { HasText = "Grace Hopper" }).Locator("input[type=checkbox]"), "Space");
        await keys.PressOnAsync(Ui.ButtonIn(picker, Text("Content.Relation.Choose")), "Enter");
        await Expect(page.Locator("[data-field='author'] .relation-field-name")).ToHaveTextAsync(["Grace Hopper"]);
        await Ui.FocusReturnedAsync(chooseAuthor);

        // The hero from the media picker, searched by typing and chosen by Enter on its tile.
        var chooseImage = page.Locator("[data-field='hero'] .media-field-actions fluent-button").Filter(Ui.Exactly(MediaUi.Text("Media.Field.Choose")));
        await keys.PressOnAsync(chooseImage, "Enter");
        var media = tab.Dialog(MediaUi.Text("Media.Picker.TitleOne"));
        await Expect(tab.ShownDialog(MediaUi.Text("Media.Picker.TitleOne"))).ToBeVisibleAsync();
        await keys.TypeIntoAsync(media.GetByLabel(MediaUi.Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true }), mark);
        await keys.PressOnAsync(Ui.ButtonIn(media, MediaUi.Text("Media.Search.Submit")), "Enter");
        await Expect(media.Locator(".media-tile-name")).ToHaveTextAsync([file]);
        await keys.PressOnAsync(media.Locator(".media-tile-open"), "Enter");
        await Expect(page.Locator("[data-field='hero'] .media-field-name")).ToHaveTextAsync([file]);
        await Ui.FocusReturnedAsync(chooseImage);

        // The rich text: typed, then Bold from the toolbar, which Alt+F10 reaches and Enter runs, back in the text.
        var body = page.Locator("[data-field='body'] [role=textbox]");
        await keys.TabToAsync(body);
        await keys.Keys.PressAsync("Control+End");
        await keys.Keys.PressAsync("Enter");
        await keys.Keys.TypeAsync("Written by keys, and ");
        await keys.Keys.PressAsync("Alt+F10");
        await Expect(page.Locator($"[data-field='body'] [role=toolbar] button[data-command=bold]")).ToBeFocusedAsync();
        await keys.Keys.PressAsync("Enter");
        await Expect(body).ToBeFocusedAsync();
        await keys.Keys.TypeAsync("bold");
        await Expect(body.Locator("strong").Filter(new LocatorFilterOptions { HasText = "bold" })).ToHaveCountAsync(1);

        // An image put in the text by the shortcut, then moved to the start by cut and paste, the way without dragging (2.5.7).
        await keys.Keys.PressAsync("End");
        await keys.Keys.PressAsync("Enter");
        await keys.Keys.PressAsync("Control+Alt+m");
        var embedding = tab.Dialog(MediaUi.Text("Media.Picker.TitleOne"));
        await Expect(tab.ShownDialog(MediaUi.Text("Media.Picker.TitleOne"))).ToBeVisibleAsync();
        await keys.TypeIntoAsync(embedding.GetByLabel(MediaUi.Text("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true }), mark);
        await keys.PressOnAsync(Ui.ButtonIn(embedding, MediaUi.Text("Media.Search.Submit")), "Enter");
        await Expect(embedding.Locator(".media-tile-name")).ToHaveTextAsync([file]);
        await keys.PressOnAsync(embedding.Locator(".media-tile-open"), "Enter");
        var embed = body.Locator(".rich-text-media");
        await Expect(embed).ToHaveClassAsync(new Regex("ProseMirror-selectednode"));
        await Expect(body).ToBeFocusedAsync();
        await keys.Keys.PressAsync("Control+x");
        await Expect(embed).ToHaveCountAsync(0);
        // The caret goes up line by line to the start; the editor reads each move from the browser a moment after the key,
        // so the next key waits for it. (Control+Home leaves the caret where it is in the headless browser.)
        await Ui.UntilAsync(
            async () =>
            {
                if (await body.EvaluateAsync<bool>("text => text.editor?.state.selection.from <= 1"))
                {
                    return true;
                }

                await keys.Keys.PressAsync("ArrowUp");
                return false;
            },
            "the caret never reached the start of the text");
        await keys.Keys.PressAsync("Control+v");
        await Expect(embed).ToHaveCountAsync(1);
        await Expect(body.Locator(":scope > *").First).ToHaveClassAsync(new Regex("rich-text-media"));

        // A quote and a gallery with four columns, the gallery moved first by Alt with the up arrow.
        var sections = page.Locator("[data-field='sections'] > fieldset");
        await AddBlockAsync(keys, sections, "Quote");
        await keys.TypeIntoAsync(sections.Locator(":scope > ul > li").Last.Locator("textarea").First, "A line to quote.");
        await AddBlockAsync(keys, sections, "Gallery");
        var gallery = sections.Locator(":scope > ul > li").Last;
        await keys.TypeIntoAsync(gallery.GetByLabel("Columns", new LocatorGetByLabelOptions { Exact = true }), "4");
        var toggle = gallery.Locator(":scope > .nested-head .nested-toggle");
        await keys.PressOnAsync(toggle, "Alt+ArrowUp");
        await Expect(sections.Locator(":scope > ul > li > .nested-head .nested-toggle > span:first-child").First).ToHaveTextAsync(Text("Content.Nested.Item", "Gallery", "1"));
        await Expect(sections.Locator(":scope > ul > li").First.Locator(":scope > .nested-head .nested-toggle")).ToBeFocusedAsync();
    }

    private static async Task AddBlockAsync(KeyboardOnly keys, ILocator sections, string label)
    {
        var count = await sections.Locator(":scope > ul > li").CountAsync();
        await keys.TabToAsync(sections.Locator(":scope > .nested-actions select"));
        await keys.Keys.TypeAsync(label);
        await keys.PressOnAsync(sections.Locator(":scope > .nested-actions fluent-button").Filter(Ui.Exactly(Text("Content.Nested.Add", label))), "Enter");
        await Expect(sections.Locator(":scope > ul > li")).ToHaveCountAsync(count + 1);
        await Expect(sections.Locator(":scope > ul > li").Last.Locator(":scope > .nested-head .nested-toggle")).ToBeFocusedAsync();
    }

    // A publish scheduled far ahead: the date and the time typed, the dialog submitted by Enter.
    private static async Task ScheduleAsync(KeyboardOnly keys, string entry)
    {
        var tab = keys.Tab;
        var open = Ui.ButtonIn(tab.Page.Locator(".content-schedule"), Text("Content.Schedule.Open"));
        await keys.PressOnAsync(open, "Enter");
        var dialog = tab.Dialog(Text("Content.Schedule.Title"));
        await Expect(tab.ShownDialog(Text("Content.Schedule.Title"))).ToBeVisibleAsync();
        await KeyboardOnly.FocusInsideAsync(dialog);
        var culture = CultureInfo.GetCultureInfo(await tab.Page.EvaluateAsync<string>("() => navigator.language"));
        await keys.TypeIntoAsync(dialog.Locator("input.cms-input-date"), new DateOnly(2099, 6, 1).ToString("d", culture));
        await keys.TypeIntoAsync(dialog.Locator("input.cms-input-time"), new TimeOnly(9, 0).ToString(culture.DateTimeFormat.ShortTimePattern, culture));
        var scheduled = await tab.AnswerAsync(() => keys.PressOnAsync(Ui.ButtonIn(dialog, Text("Content.Schedule.Submit")), "Enter"), "POST", $"{entry}/schedule");
        scheduled.Status.ShouldBe(200);
        JsonNode.Parse(scheduled.Request.PostData!)!["at"]!.GetValue<string>().ShouldBe("2099-06-01T09:00:00");
        await Expect(tab.ShownDialog(Text("Content.Schedule.Title"))).ToBeHiddenAsync();
    }

    // The showcase entry's preview pane by its button, which takes focus, and its width by the select.
    private async Task PreviewAsync(KeyboardOnly keys, Guid showcase)
    {
        var page = keys.Tab.Page;
        await page.GotoAsync(new Uri(Host.Admin, $"content/showcase/{showcase:D}").AbsoluteUri);
        await keys.PressOnAsync(Ui.ButtonIn(page.Locator(".content-form-history"), Text("Content.Preview.Open")), "Enter");
        var pane = page.Locator(".content-preview");
        await Expect(pane.Locator("iframe")).ToHaveAttributeAsync("src", new Regex($@"/showcase/{showcase:D}\?preview="));
        await Expect(pane).ToBeFocusedAsync();
        await keys.TabToAsync(page.GetByLabel(Text("Content.Preview.Width"), new PageGetByLabelOptions { Exact = true }));
        await keys.Keys.TypeAsync(Text("Content.Preview.Tablet"));
        await Expect(pane).ToHaveAttributeAsync("data-width", "tablet");
    }

    // Every row by the header's checkbox and Space, then Publish from the selection's menu, confirmed in its dialog.
    private static async Task BulkPublishAsync(KeyboardOnly keys, ILocator grid, int count)
    {
        var tab = keys.Tab;
        await keys.PressOnAsync(grid.Locator("thead button[role=checkbox]"), "Space");
        var selection = tab.Page.Locator(".content-list-selection");
        await Expect(selection).ToContainTextAsync(Text("Content.List.PageSelected", $"{count}"));
        await keys.PressOnAsync(selection.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = Text("Content.Bulk.Menu"), Exact = true }), "Enter");
        await keys.ArrowToAsync(tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Text("Content.Bulk.Publish"), Exact = true }));
        await keys.Keys.PressAsync("Enter");
        var confirm = tab.Dialog(Text("Content.Bulk.Publish"));
        await Expect(tab.ShownDialog(Text("Content.Bulk.Publish"))).ToBeVisibleAsync();
        await KeyboardOnly.FocusInsideAsync(confirm);
        await keys.PressOnAsync(Ui.ButtonIn(confirm, Text("Content.Bulk.Publish")), "Enter");
        var progressTitle = Text("Content.Bulk.Title", Text("Content.Bulk.Publish"), "Article");
        var progress = tab.Dialog(progressTitle);
        await Expect(progress.Locator(".bulk-progress-summary")).ToHaveTextAsync(Text("Content.Bulk.Ended", $"{count}", "0"));
        await keys.PressOnAsync(Ui.ButtonIn(progress, Text("Content.Bulk.Close")), "Enter");
        await Expect(tab.ShownDialog(progressTitle)).ToBeHiddenAsync();
    }

    // The gallery's dialog and drawer close by their close buttons, a dialog taken out of the page while open gives focus
    // back too, and the toasts are read from the shell's regions alone, the error one staying until it is dismissed.
    private async Task GalleryAsync(KeyboardOnly keys)
    {
        var tab = keys.Tab;
        var page = tab.Page;
        await page.GotoAsync(new Uri(Host.Admin, "system/components").AbsoluteUri);
        await Expect(page.Locator("main h1")).ToHaveTextAsync(Gallery("System.Components.Heading"));
        var close = $"fluent-button[aria-label='{Repository.ComponentText("Close")}']";

        var open = Ui.ButtonIn(page.Locator("main"), Gallery("System.Components.OpenDialog"));
        var title = Gallery("System.Components.DialogTitle");
        await keys.PressOnAsync(open, "Enter");
        await Expect(tab.ShownDialog(title)).ToBeVisibleAsync();
        await Expect(Ui.ButtonIn(tab.Dialog(title), Gallery("System.Components.Discard"))).ToBeFocusedAsync();
        await keys.PressOnAsync(tab.Dialog(title).Locator(close), "Enter");
        await Expect(tab.ShownDialog(title)).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(open);

        await keys.PressOnAsync(open, "Enter");
        await Expect(tab.ShownDialog(title)).ToBeVisibleAsync();
        await keys.PressOnAsync(Ui.ButtonIn(tab.Dialog(title), Gallery("System.Components.RemoveDialog")), "Enter");
        await Expect(tab.Dialog(title)).ToHaveCountAsync(0);
        await Ui.FocusReturnedAsync(open);

        var drawer = Ui.ButtonIn(page.Locator("main"), Gallery("System.Components.OpenDrawer"));
        var filters = page.Locator($"fluent-drawer[aria-label=\"{Gallery("System.Components.Filters")}\"]");
        await keys.PressOnAsync(drawer, "Enter");
        await Expect(tab.ShownDialog(Gallery("System.Components.Filters"))).ToBeVisibleAsync();
        await Expect(filters.GetByLabel(Gallery("System.Components.Search"), new LocatorGetByLabelOptions { Exact = true })).ToBeFocusedAsync();
        await keys.PressOnAsync(filters.Locator(close), "Enter");
        await Expect(tab.ShownDialog(Gallery("System.Components.Filters"))).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(drawer);

        await keys.PressOnAsync(Ui.ButtonIn(page.Locator("main"), Gallery("System.Components.ShowErrorToast")), "Enter");
        await keys.PressOnAsync(Ui.ButtonIn(page.Locator("main"), Gallery("System.Components.ShowToast")), "Enter");
        await Expect(Region(page, "assertive", Gallery("System.Components.SaveFailed"))).ToHaveCountAsync(1);
        await Expect(Region(page, "polite", Gallery("System.Components.Saved"))).ToHaveCountAsync(1);
        await Expect(page.Locator("[aria-live]")).ToHaveCountAsync(2);
        var error = page.Locator(".cms-toast[data-intent='error']");
        await Expect(error).ToContainTextAsync(Gallery("System.Components.SaveFailed"));
        await keys.PressOnAsync(error.Locator(close), "Enter");
        await Expect(error).ToHaveCountAsync(0);
    }

    // Sign-out from the user menu, confirmed on the hosted page, which ends at its sign-in.
    private static async Task SignOutAsync(KeyboardOnly keys, Editor editor)
    {
        var page = keys.Tab.Page;
        await keys.PressOnAsync(UserMenu(page, editor), "Enter");
        await keys.ArrowToAsync(page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Text("Shell.UserMenu.SignOut"), Exact = true }));
        await keys.Keys.PressAsync("Enter");
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/account/logout");
        await keys.PressOnAsync(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out", Exact = true }), "Enter");
        await Expect(keys.Tab.LoginField).ToBeVisibleAsync();
    }

    // The hosted page's fields by Tab, the password typed and masked from the trace, and Enter to send.
    private static async Task SignInAsync(AdminBrowser browser, KeyboardOnly keys, Editor editor)
    {
        browser.Mask(editor.Password);
        var page = keys.Tab.Page;
        await keys.TypeIntoAsync(page.GetByLabel("User name or email"), editor.Email);
        await keys.TabToAsync(page.GetByLabel("Password", new PageGetByLabelOptions { Exact = true }));
        await keys.Keys.TypeAsync(editor.Password);
        await keys.Keys.PressAsync("Enter");
    }

    // Three articles of the case and a showcase entry with the preview template of PreviewTests.
    private async Task<(Guid[] Articles, Guid Showcase)> MakeAsync(string mark)
    {
        using var api = await Host.ManagementClientAsync();
        var articles = new List<Guid>();
        foreach (var n in new[] { 1, 2, 3 })
        {
            articles.Add(await CreateAsync(api, "content/article", new JsonObject { ["title"] = $"Keys {mark} {n}" }));
        }

        using (var read = await api.GetAsync(new Uri("api/management/v1/settings/preview/defaults", UriKind.Relative), Token))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = new JsonObject { ["templates"] = new JsonObject { ["showcase"] = PreviewTemplate } }.ToJsonString();
            using var write = new HttpRequestMessage(HttpMethod.Put, new Uri("api/management/v1/settings/preview/defaults", UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            write.Headers.IfMatch.Add(read.Headers.ETag ?? new EntityTagHeaderValue("\"0\""));
            using var written = await api.SendAsync(write, Token);
            written.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var showcase = await CreateAsync(api, "content/showcase", new JsonObject { ["title"] = $"Showcase {mark}" });
        return ([.. articles], showcase);
    }

    // Another writer's save of the title, with the version it read.
    private async Task SaveElsewhereAsync(Guid id, string title)
    {
        using var api = await Host.ManagementClientAsync();
        var path = new Uri($"api/management/v1/content/article/{id:D}", UriKind.Relative);
        using var read = await api.GetAsync(path, Token);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var write = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(new JsonObject { ["title"] = title }) };
        write.Headers.IfMatch.Add(read.Headers.ETag.ShouldNotBeNull());
        using var written = await api.SendAsync(write, Token);
        written.StatusCode.ShouldBe(HttpStatusCode.OK, await written.Content.ReadAsStringAsync(Token));
    }

    private static async Task<Guid> CreateAsync(HttpClient api, string path, JsonObject body)
    {
        using var created = await api.PostAsJsonAsync(new Uri("api/management/v1/" + path, UriKind.Relative), body, Token);
        var answer = await created.Content.ReadAsStringAsync(Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, answer);
        return JsonNode.Parse(answer)!["id"]!.GetValue<Guid>();
    }

    private static ILocator UserMenu(IPage page, Editor editor) => page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email });

    private static ILocator Theme(IPage page, string key) =>
        page.GetByRole(AriaRole.Menuitemradio, new PageGetByRoleOptions { Name = Text(key), Exact = true });

    private static ILocator Toolbar(AdminTab tab, string key) => Ui.ButtonIn(tab.Page.Locator(".content-form-toolbar"), Text(key)).First;

    private static ILocator Row(ILocator grid, string title) =>
        grid.Locator("tbody tr").Filter(new LocatorFilterOptions { Has = grid.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = title, Exact = true }) });

    private static ILocator Region(IPage page, string politeness, string text) =>
        page.Locator($"[aria-live={politeness}] > div").Filter(Ui.Exactly(text));

    private static string Gallery(string key) => Repository.AdministrationText("Gallery", key);

    private static string Text(string key, params string[] arguments) => Ui.Format(Repository.AdminText(key), arguments);
}
