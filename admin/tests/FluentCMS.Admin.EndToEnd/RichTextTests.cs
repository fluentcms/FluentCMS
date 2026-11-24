using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A-24: the built-in Editor writes an article body on the Blog sample with the keyboard alone, through Markdown shortcuts, the
// toolbar and the link panel, pastes HTML that keeps only what the schema allows, saves, and reads the body back unchanged.
// The editor's module is fetched with the first form that has a rich text field, not with the start page.
public sealed class RichTextTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private const string Pasted =
        """<p>Pasted <span style="color:red">red</span> <a href="javascript:alert(1)">bad</a><img src="x.png"> <a href="https://example.org/">good</a></p><table><tr><td>cell</td></tr></table>""";

    // Not on WebKit (spec 7): after Ctrl+K on a selection and an address the selected text is gone, with no link.
    [Fact]
    [Trait("NotOn", "webkit")]
    public async Task TheEditorWritesABodyByKeyboardSavesItAndReadsItBackUnchanged()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        // Playwright raises the event on a thread of its own.
        var requested = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) => requested.Enqueue(request.Url);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        requested.ShouldNotContain(url => url.Contains("FluentCMS.Admin.RichText", StringComparison.Ordinal));

        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Written with rich text {Mark}");
        var body = tab.Page.Locator("[data-field='body'] [role=textbox]");
        await Expect(body).ToBeVisibleAsync();
        requested.ShouldContain(url => url.EndsWith("/_content/FluentCMS.Admin.RichText/rich-text.js", StringComparison.Ordinal));
        await Expect(body).ToHaveAttributeAsync("aria-labelledby", new Regex("."));

        var keyboard = tab.Page.Keyboard;
        await body.ClickAsync();
        await keyboard.TypeAsync("# Rich text");
        await keyboard.PressAsync("Enter");
        await keyboard.TypeAsync("Some ");
        await keyboard.PressAsync("Control+b");
        await keyboard.TypeAsync("bold");
        await keyboard.PressAsync("Control+b");
        await keyboard.TypeAsync(" and a link");
        for (var index = 0; index < "a link".Length; index++)
        {
            await keyboard.PressAsync("Shift+ArrowLeft");
        }

        await keyboard.PressAsync("Control+k");
        var address = tab.Page.GetByLabel(Repository.RichTextText("RichText.Link.Address"), new PageGetByLabelOptions { Exact = true });
        await Expect(address).ToBeFocusedAsync();
        await keyboard.TypeAsync("https://example.com/");
        await keyboard.PressAsync("Enter");
        await Expect(address).ToHaveCountAsync(0);
        await Expect(body).ToBeFocusedAsync();
        await keyboard.PressAsync("End");
        await keyboard.PressAsync("Enter");
        await keyboard.TypeAsync("- first");
        await keyboard.PressAsync("Enter");
        await keyboard.TypeAsync("second");
        await keyboard.PressAsync("Enter");
        await keyboard.PressAsync("Enter");

        // The toolbar by keyboard: Alt+F10 to its first button, the arrows along it, Enter to run one, which returns to the text.
        await keyboard.PressAsync("Alt+F10");
        await Expect(ToolbarButton(tab, "RichText.Toolbar.Bold")).ToBeFocusedAsync();
        await keyboard.PressAsync("ArrowRight");
        await Expect(ToolbarButton(tab, "RichText.Toolbar.Italic")).ToBeFocusedAsync();
        await Expect(ToolbarButton(tab, "RichText.Toolbar.Bold")).ToHaveAttributeAsync("tabindex", "-1");
        await keyboard.PressAsync("Enter");
        await Expect(body).ToBeFocusedAsync();
        await keyboard.TypeAsync("slanted");
        await keyboard.PressAsync("Alt+F10");
        await Expect(ToolbarButton(tab, "RichText.Toolbar.Italic")).ToBeFocusedAsync();
        await keyboard.PressAsync("Escape");
        await Expect(body).ToBeFocusedAsync();
        await keyboard.PressAsync("Control+i");
        await keyboard.PressAsync("Enter");
        await body.EvaluateAsync("(element, html) => { const data = new DataTransfer(); data.setData('text/html', html); data.setData('text/plain', 'Pasted'); const paste = new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }); Object.defineProperty(paste, 'clipboardData', { value: data }); element.dispatchEvent(paste); }", Pasted);
        await Expect(body).ToContainTextAsync("cell");

        var create = await SaveAsync(tab, "POST", tab.ManagementUrl("content/article"));

        create.Status.ShouldBe(201);
        var written = JsonNode.Parse(create.Request.PostData!)!["body"]!;
        var blocks = written["content"]!.AsArray();
        blocks[0]!.ToJsonString().ShouldBe("""{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"Rich text"}]}""");
        blocks[1]!.ToJsonString().ShouldBe(
            """{"type":"paragraph","content":[{"type":"text","text":"Some "},{"type":"text","marks":[{"type":"bold"}],"text":"bold"},{"type":"text","text":" and "},""" +
            """{"type":"text","marks":[{"type":"link","attrs":{"href":"https://example.com/"}}],"text":"a link"}]}""");
        blocks[2]!["type"]!.GetValue<string>().ShouldBe("bulletList");
        blocks[2]!["content"]!.AsArray().Count.ShouldBe(2);
        blocks[3]!.ToJsonString().ShouldBe("""{"type":"paragraph","content":[{"type":"text","marks":[{"type":"italic"}],"text":"slanted"}]}""");

        // The paste: text and a safe link stay; the colour, the unsafe link, the image and the table do not.
        var pasted = string.Join(",", blocks.Skip(4).Select(block => block!.ToJsonString()));
        pasted.ShouldContain("Pasted red bad");
        pasted.ShouldContain("""{"type":"text","marks":[{"type":"link","attrs":{"href":"https://example.org/"}}],"text":"good"}""");
        pasted.ShouldContain("cell");
        pasted.ShouldNotContain("javascript");
        pasted.ShouldNotContain("color");
        pasted.ShouldNotContain("x.png");
        pasted.ShouldNotContain("table");

        // Read back: the reloaded form holds the body the server read, which the next save sends as it is.
        var id = await Ui.CreatedIdAsync(create, "content/article");
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await tab.Page.ReloadAsync();
        await Expect(body).ToContainTextAsync("slanted");
        await Expect(body.Locator("h1")).ToHaveTextAsync("Rich text");
        await Expect(body.Locator("a[href='https://example.com/']")).ToHaveTextAsync("a link");
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"Read back with rich text {Mark}");

        var save = await SaveAsync(tab, "PUT", tab.ManagementUrl($"content/article/{id:D}"));

        save.Status.ShouldBe(200);
        JsonNode.DeepEquals(JsonNode.Parse(save.Request.PostData!)!["body"], written).ShouldBeTrue();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A-25: an entry link chosen with the relation picker and an image embedded with the media picker, by the keyboard, with
    // the image's alternative text set in its dialog; the server takes the body, reads it back unchanged, and lists the article
    // as a use of the image, which it extracted from the body.
    [Fact]
    public async Task TheEditorLinksAnEntryAndEmbedsAnImageAndTheServerKeepsAndExtractsThem()
    {
        var mark = Mark;
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());
        var title = $"{mark} links and embeds";
        await tab.Page.Locator("[data-field='title'] input").FillAsync(title);
        var body = tab.Page.Locator("[data-field='body'] [role=textbox]");
        await Expect(body).ToBeVisibleAsync();
        var keyboard = tab.Page.Keyboard;
        await body.ClickAsync();
        await keyboard.TypeAsync("Read ");

        // The entry link: its panel by the shortcut, the type, and the relation picker in a dialog.
        await keyboard.PressAsync("Control+Alt+k");
        var types = tab.Page.GetByLabel(Repository.RichTextText("RichText.EntryLink.Type"), new PageGetByLabelOptions { Exact = true });
        await Expect(types).ToBeFocusedAsync();
        await types.SelectOptionAsync("tag");
        await keyboard.PressAsync("Tab");
        var choose = tab.Page.Locator(".rich-text-link fluent-button").Filter(Exactly(Repository.RichTextText("RichText.EntryLink.Choose")));
        await Expect(choose).ToBeFocusedAsync();
        await keyboard.PressAsync("Enter");
        var relations = tab.Page.Locator("fluent-dialog[aria-label=\"Choose an entry: Tag\"]");
        var dotnet = relations.Locator(".cms-data-grid tbody tr").Filter(new LocatorFilterOptions { HasText = "dotnet" });
        await Expect(dotnet).ToBeVisibleAsync();
        await dotnet.Locator("input[type=checkbox]").CheckAsync();
        await relations.Locator("fluent-button").Filter(Exactly(Repository.AdminText("Content.Relation.Choose"))).ClickAsync();
        await Expect(relations).ToBeHiddenAsync();
        var link = body.Locator(".rich-text-entry-link");
        await Expect(link).ToHaveTextAsync("dotnet");
        await Expect(link.GetByTitle("Tag: dotnet", new LocatorGetByTitleOptions { Exact = true })).ToHaveTextAsync("dotnet");
        await FocusInTextAsync(body);

        // The image: the media picker by the shortcut in an empty paragraph, an upload chosen, its preview in the text.
        await keyboard.PressAsync("End");
        await keyboard.PressAsync("Enter");
        await keyboard.PressAsync("Control+Alt+m");
        var media = tab.Page.Locator($"fluent-dialog[aria-label=\"{Repository.MediaText("Media.Picker.TitleOne")}\"]");
        await media.GetByLabel(Repository.MediaText("Media.Search.Label"), new LocatorGetByLabelOptions { Exact = true }).FillAsync(mark);
        await media.Locator("fluent-button").Filter(Exactly(Repository.MediaText("Media.Search.Submit"))).ClickAsync();
        await media.Locator("[data-media-upload] input[type=file]").SetInputFilesAsync(new FilePayload { Name = $"{mark}.png", MimeType = "image/png", Buffer = Convert.FromBase64String(Png) });
        await Expect(media.Locator(".media-upload-done")).ToHaveCountAsync(1);
        await media.Locator(".media-tile-open").Filter(new LocatorFilterOptions { Has = tab.Page.Locator(".media-tile-name", new PageLocatorOptions { HasTextString = $"{mark}.png" }) }).ClickAsync();
        await Expect(media).ToBeHiddenAsync();
        var embed = body.Locator(".rich-text-media");
        await Expect(embed).ToHaveAttributeAsync("aria-label", $"{Repository.RichTextText("RichText.Kept.Media")}: {mark}.png");
        await Expect(embed.Locator("img")).Not.ToHaveJSPropertyAsync("naturalWidth", 0);
        await Expect(embed).ToHaveClassAsync(new Regex("ProseMirror-selectednode"));
        await FocusInTextAsync(body);

        // Its alternative text, in its dialog by Enter on the selected embed.
        await keyboard.PressAsync("Enter");
        var dialog = tab.Page.Locator($"fluent-dialog[aria-label=\"{Repository.RichTextText("RichText.Media.Title")}\"]");
        var alt = dialog.GetByLabel(Repository.RichTextText("RichText.Media.Alt"), new LocatorGetByLabelOptions { Exact = true });
        await Expect(alt).ToBeVisibleAsync();
        await Ui.FocusAsync(alt);
        await keyboard.TypeAsync("A small square");
        await keyboard.PressAsync("Tab");
        await Expect(dialog.Locator("fluent-button").Filter(Exactly(Repository.RichTextText("RichText.Media.Done")))).ToBeFocusedAsync();
        await keyboard.PressAsync("Enter");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(embed).ToHaveAttributeAsync("aria-label", $"{Repository.RichTextText("RichText.Kept.Media")}: A small square");
        await FocusInTextAsync(body);

        var create = await SaveAsync(tab, "POST", tab.ManagementUrl("content/article"));

        create.Status.ShouldBe(201);
        var written = JsonNode.Parse(create.Request.PostData!)!["body"]!;
        var blocks = written["content"]!.AsArray();
        blocks.Count.ShouldBe(2);
        var linked = blocks[0]!["content"]!.AsArray();
        linked[0]!.ToJsonString().ShouldBe("""{"type":"text","text":"Read "}""");
        linked[1]!["text"]!.GetValue<string>().ShouldBe("dotnet");
        var entryLink = linked[1]!["marks"]!.AsArray().Single()!;
        entryLink["type"]!.GetValue<string>().ShouldBe("entryLink");
        entryLink["attrs"]!["entryType"]!.GetValue<string>().ShouldBe("tag");
        Guid.TryParse(entryLink["attrs"]!["entryId"]!.GetValue<string>(), out _).ShouldBeTrue();
        entryLink["attrs"]!.AsObject().Count.ShouldBe(2);
        blocks[1]!["type"]!.GetValue<string>().ShouldBe("media");
        blocks[1]!["attrs"]!["alt"]!.GetValue<string>().ShouldBe("A small square");
        Guid.TryParse(blocks[1]!["attrs"]!["mediaId"]!.GetValue<string>(), out _).ShouldBeTrue();
        blocks[1]!["attrs"]!.AsObject().Count.ShouldBe(2);

        // Read back: the reloaded form shows both and the next save sends the body unchanged.
        var location = await create.HeaderValueAsync("location");
        var id = Guid.Parse(location.ShouldNotBeNull()[(location.LastIndexOf('/') + 1)..].Split('?')[0]);
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await tab.Page.ReloadAsync();
        await Expect(body.Locator(".rich-text-entry-link").GetByTitle("Tag: dotnet", new LocatorGetByTitleOptions { Exact = true })).ToHaveTextAsync("dotnet");
        await Expect(body.Locator(".rich-text-media img")).Not.ToHaveJSPropertyAsync("naturalWidth", 0);
        await tab.Page.Locator("[data-field='title'] input").FillAsync($"{title} again");

        var save = await SaveAsync(tab, "PUT", tab.ManagementUrl($"content/article/{id:D}"));

        save.Status.ShouldBe(200);
        JsonNode.DeepEquals(JsonNode.Parse(save.Request.PostData!)!["body"], written).ShouldBeTrue();

        // The server extracted the image from the body: the library lists the article as a use of it.
        await tab.Page.GotoAsync(new Uri(Host.Admin, "media").AbsoluteUri);
        await tab.Page.GetByLabel(Repository.MediaText("Media.Search.Label"), new PageGetByLabelOptions { Exact = true }).FillAsync(mark);
        await tab.Page.Locator("fluent-button").Filter(Exactly(Repository.MediaText("Media.Search.Submit"))).First.ClickAsync();
        await Expect(tab.Page.Locator(".media-tile-name")).ToHaveTextAsync([$"{mark}.png"]);
        await tab.Page.Locator(".media-tile-open").ClickAsync();
        await Expect(tab.Page.Locator(".cms-drawer .media-panel-usage a")).ToHaveTextAsync([$"{title} again"]);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The bundle itself on a page of the admin's origin, without the form: what the editor cannot hold whole is refused, a
    // focus and blur on a document whose marks it reorders sends nothing, and the clipboard keeps entry links, media and
    // inline blocks within the editor (a copy's inline block takes a fresh id, a cut one keeps its own) and never makes them
    // from HTML pasted from elsewhere.
    [Fact]
    public async Task TheBundleRefusesWhatItCannotHoldAndKeepsKeptNodesThroughTheClipboard()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var page = tab.Page;
        await page.GotoAsync(new Uri(Host.Admin, "_content/FluentCMS.Admin.RichText/rich-text.css").AbsoluteUri);
        await page.EvaluateAsync(
            """
            async () => {
                const module = await import(new URL("rich-text.js", location.href).href);
                window.calls = [];
                const host = { invokeMethodAsync: (method, ...args) => { window.calls.push([method, ...args]); return Promise.resolve(); } };
                window.make = async (json, id) => {
                    const element = document.createElement("div");
                    document.body.appendChild(element);
                    return await module.create(element, null, host, JSON.stringify(json), {
                        editable: true, textboxId: id, labelledBy: "label", describedBy: null, lang: "en", dir: "ltr", invalid: false,
                        labels: { media: "Media", inlineBlock: "Block" },
                    });
                };
                window.changes = () => window.calls.filter(call => call[0] === "OnChangeAsync").length;
            }
            """);

        var refused = await page.EvaluateAsync<bool[]>(
            """
            async () => (await Promise.all([
                window.make({ type: "doc", content: [{ type: "paragraph", attrs: { textAlign: "center" } }] }, "refused-1"),
                window.make({ type: "doc", content: [{ type: "table" }] }, "refused-2"),
                window.make({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a", marks: [{ type: "highlight" }] }] }] }, "refused-3"),
            ])).map(handle => handle === null)
            """);
        refused.ShouldBe([true, true, true]);
        (await page.Locator("[role=textbox]").CountAsync()).ShouldBe(0);

        await page.EvaluateAsync(
            """async () => { window.reordered = await window.make({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "a", marks: [{ type: "bold" }, { type: "link", attrs: { href: "/" } }] }] }] }, "reordered"); }""");
        var reordered = page.Locator("#reordered");
        await reordered.FocusAsync();
        await Expect(reordered).ToBeFocusedAsync();
        await reordered.BlurAsync();
        await Expect(reordered).Not.ToBeFocusedAsync();
        (await page.EvaluateAsync<int>("() => window.changes()")).ShouldBe(0);
        (await page.EvaluateAsync<bool>("() => window.reordered.flush().changed")).ShouldBeFalse();

        await page.EvaluateAsync(
            """
            async () => {
                window.kept = await window.make({ type: "doc", content: [{ type: "paragraph", content: [
                    { type: "text", text: "See " },
                    { type: "text", marks: [{ type: "entryLink", attrs: { entryId: "0b9f4a52-7c1e-4d0a-9a43-5e1f6b2c8d70", entryType: "article" } }], text: "other" },
                    { type: "media", attrs: { mediaId: "5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a" } },
                    { type: "inlineBlock", attrs: { blockType: "note", id: "Nq8vB2xK9pLm", data: { n: 1 } } },
                ] }] }, "kept");
                window.clipboard = (target, type, data) => {
                    const transfer = new DataTransfer();
                    for (const [format, text] of Object.entries(data ?? {})) {
                        transfer.setData(format, text);
                    }

                    const event = new ClipboardEvent(type, { clipboardData: transfer, bubbles: true, cancelable: true });
                    // Firefox gives an event made by a script an empty clipboard of its own, whatever the event was made with.
                    Object.defineProperty(event, "clipboardData", { value: transfer });
                    target.dispatchEvent(event);
                    return { html: transfer.getData("text/html"), text: transfer.getData("text/plain") };
                };
                // The editor's document: the change it holds back, else the last one it sent.
                window.document_ = () => {
                    const pending = window.kept.flush();
                    if (pending.changed) {
                        window.calls.push(["OnChangeAsync", pending.value]);
                    }

                    return JSON.parse(window.calls.filter(call => call[0] === "OnChangeAsync").at(-1)[1]);
                };
                window.inline = document => document.content.flatMap(block => block.content ?? []);
            }
            """);
        var kept = page.Locator("#kept");

        // Copy and paste: everything is copied and pasted at the end, beside the original.
        await kept.FocusAsync();
        await SelectAsync(page, "Control+a", "selection.from === 0 && selection.to === size");
        var copiedHtml = await page.EvaluateAsync<string>("() => { window.copied = window.clipboard(document.getElementById('kept'), 'copy'); return window.copied.html; }");
        copiedHtml.ShouldContain("data-block-id=\"Nq8vB2xK9pLm\"");
        copiedHtml.ShouldContain("data-media-id=\"5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a\"");
        copiedHtml.ShouldContain("data-entry-id=\"0b9f4a52-7c1e-4d0a-9a43-5e1f6b2c8d70\"");
        await SelectAsync(page, "Control+End", "selection.empty && selection.from === size - 1");
        await page.EvaluateAsync("() => window.clipboard(document.getElementById('kept'), 'paste', { 'text/html': window.copied.html, 'text/plain': window.copied.text })");
        var copied = JsonNode.Parse(await page.EvaluateAsync<string>("() => JSON.stringify(window.inline(window.document_()))"))!.AsArray();
        var blocks = copied.Where(node => node!["type"]!.GetValue<string>() == "inlineBlock").ToList();
        blocks.Count.ShouldBe(2);
        blocks[0]!["attrs"]!["id"]!.GetValue<string>().ShouldBe("Nq8vB2xK9pLm");
        blocks[1]!["attrs"]!["id"]!.GetValue<string>().ShouldMatch("^[A-Za-z0-9_-]{12}$");
        blocks[1]!["attrs"]!["id"]!.GetValue<string>().ShouldNotBe("Nq8vB2xK9pLm");
        blocks[1]!["attrs"]!["data"]!.ToJsonString().ShouldBe("""{"n":1}""");
        copied.Count(node => node!["type"]!.GetValue<string>() == "media").ShouldBe(2);
        copied.Count(node => node!["marks"]?.AsArray().Any(mark => mark!["type"]!.GetValue<string>() == "entryLink") == true).ShouldBe(2);

        // Cut and paste: the copy is cut and pasted at the start, and keeps the id it was given.
        var moved = blocks[1]!["attrs"]!["id"]!.GetValue<string>();
        var copy = kept.Locator($"[data-block-id='{moved}']");
        await copy.ClickAsync();
        await Expect(copy).ToHaveClassAsync(new Regex("ProseMirror-selectednode"));
        await page.WaitForFunctionAsync("id => document.getElementById('kept').editor.state.selection.node?.attrs.id === id", moved);
        var cutHtml = await page.EvaluateAsync<string>("() => { window.cut = window.clipboard(document.getElementById('kept'), 'cut'); return window.cut.html; }");
        cutHtml.ShouldContain($"data-block-id=\"{moved}\"");
        cutHtml.ShouldNotContain("Nq8vB2xK9pLm");
        await Expect(copy).ToHaveCountAsync(0);
        await SelectAsync(page, "Control+Home", "selection.empty && selection.from === 1");
        await page.EvaluateAsync("() => window.clipboard(document.getElementById('kept'), 'paste', { 'text/html': window.cut.html, 'text/plain': window.cut.text })");
        var ids = await page.EvaluateAsync<string[]>("() => window.inline(window.document_()).filter(node => node.type === 'inlineBlock').map(node => node.attrs.id)");
        ids.ShouldBe([moved, "Nq8vB2xK9pLm"]);

        // HTML from elsewhere: the same elements without this editor's origin make plain text.
        await SelectAsync(page, "Control+End", "selection.empty && selection.from === size - 1");
        await page.EvaluateAsync(
            """
            () => window.clipboard(document.getElementById('kept'), 'paste', { 'text/html':
                '<span data-fluentcms-media data-media-id="a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d">picture</span>' +
                '<span data-entry-id="7d2e6c11-3a5b-4f80-b9c4-2a6d1e8f0b35" data-entry-type="author">someone</span>' +
                '<span data-fluentcms-block data-block-type="note" data-block-id="foreign">block</span>' })
            """);
        var after = JsonNode.Parse(await page.EvaluateAsync<string>("() => JSON.stringify(window.document_())"))!.ToJsonString();
        after.ShouldNotContain("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");
        after.ShouldNotContain("7d2e6c11-3a5b-4f80-b9c4-2a6d1e8f0b35");
        after.ShouldNotContain("foreign");
        after.ShouldContain("someone");
    }

    // Focus is in the text once a dialog has closed. After the relation picker its opener, the panel's button, is gone, so focus
    // reaches the text only by the editor's return of focus, which comes after the component library's own; after the media
    // picker and the media dialog the opener is the text itself. The keys that follow land in the text, which the body checks.
    private static Task FocusInTextAsync(ILocator text) => Expect(text).ToBeFocusedAsync();

    private static LocatorFilterOptions Exactly(string text) => Ui.Exactly(text);

    // A key that changes the selection of the editor #kept, and the wait until the editor's own state has the selection that
    // `taken` describes, of `selection` in a document of `size`; Tiptap keeps the editor on its element. The browser's
    // selectionchange can come before ProseMirror has read it, so it is no sign that the editor has.
    private static async Task SelectAsync(IPage page, string key, string taken)
    {
        await page.Keyboard.PressAsync(key);
        await page.WaitForFunctionAsync(
            $"() => {{ const state = document.getElementById('kept').editor.state; const selection = state.selection; const size = state.doc.content.size; return {taken}; }}");
    }

    private static ILocator ToolbarButton(AdminTab tab, string resourceKey) =>
        tab.Page.Locator($"[role=toolbar] button[aria-label='{Repository.RichTextText(resourceKey)}']");

    private static Task<IResponse> SaveAsync(AdminTab tab, string method, string url) => tab.AnswerAsync(() => tab.Button("Content.Form.Save").ClickAsync(), method, url);
}
