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
    private const string Pasted =
        """<p>Pasted <span style="color:red">red</span> <a href="javascript:alert(1)">bad</a><img src="x.png"> <a href="https://example.org/">good</a></p><table><tr><td>cell</td></tr></table>""";

    [Fact]
    public async Task TheEditorWritesABodyByKeyboardSavesItAndReadsItBackUnchanged()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        // Playwright raises the event on a thread of its own.
        var requested = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) => requested.Enqueue(request.Url);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        requested.ShouldNotContain(url => url.Contains("FluentCMS.Admin.RichText", StringComparison.Ordinal));

        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article/new").AbsoluteUri);
        await tab.Page.Locator("[data-field='title'] input").FillAsync("Written with rich text");
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
        await body.EvaluateAsync("(element, html) => { const data = new DataTransfer(); data.setData('text/html', html); data.setData('text/plain', 'Pasted'); element.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true })); }", Pasted);
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
        var location = await create.HeaderValueAsync("location");
        var id = Guid.Parse(location.ShouldNotBeNull()[(location.LastIndexOf('/') + 1)..]);
        await Expect(tab.Page).ToHaveURLAsync(new Regex($"/admin/content/article/{id:D}\\?locale=en$"), new PageAssertionsToHaveURLOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await tab.Page.ReloadAsync();
        await Expect(body).ToContainTextAsync("slanted");
        await Expect(body.Locator("h1")).ToHaveTextAsync("Rich text");
        await Expect(body.Locator("a[href='https://example.com/']")).ToHaveTextAsync("a link");
        await tab.Page.Locator("[data-field='title'] input").FillAsync("Read back with rich text");

        var save = await SaveAsync(tab, "PUT", tab.ManagementUrl($"content/article/{id:D}"));

        save.Status.ShouldBe(200);
        JsonNode.DeepEquals(JsonNode.Parse(save.Request.PostData!)!["body"], written).ShouldBeTrue();
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

                    target.dispatchEvent(new ClipboardEvent(type, { clipboardData: transfer, bubbles: true, cancelable: true }));
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
                // ProseMirror takes a selection the browser moved on selectionchange; this listener, added after its own,
                // counts the ones it has taken.
                window.selections = 0;
                document.addEventListener("selectionchange", () => window.selections++);
                window.inline = document => document.content.flatMap(block => block.content ?? []);
            }
            """);
        var kept = page.Locator("#kept");

        // Copy and paste: everything is copied and pasted at the end, beside the original.
        await kept.FocusAsync();
        await page.Keyboard.PressAsync("Control+a");
        await page.EvaluateAsync("() => { window.copied = window.clipboard(document.getElementById('kept'), 'copy'); }");
        await MoveAsync(page, "Control+End");
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
        await page.EvaluateAsync("() => { window.cut = window.clipboard(document.getElementById('kept'), 'cut'); }");
        await Expect(copy).ToHaveCountAsync(0);
        await MoveAsync(page, "Control+Home");
        await page.EvaluateAsync("() => window.clipboard(document.getElementById('kept'), 'paste', { 'text/html': window.cut.html, 'text/plain': window.cut.text })");
        var ids = await page.EvaluateAsync<string[]>("() => window.inline(window.document_()).filter(node => node.type === 'inlineBlock').map(node => node.attrs.id)");
        ids.ShouldBe([moved, "Nq8vB2xK9pLm"]);

        // HTML from elsewhere: the same elements without this editor's origin make plain text.
        await MoveAsync(page, "Control+End");
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

    // A key that moves the caret, and the wait until ProseMirror has taken the browser's new selection.
    private static async Task MoveAsync(IPage page, string key)
    {
        var before = await page.EvaluateAsync<int>("() => window.selections");
        await page.Keyboard.PressAsync(key);
        await page.WaitForFunctionAsync("before => window.selections > before", before);
    }


    private static ILocator ToolbarButton(AdminTab tab, string resourceKey) =>
        tab.Page.Locator($"[role=toolbar] button[aria-label='{Repository.RichTextText(resourceKey)}']");

    private static Task<IResponse> SaveAsync(AdminTab tab, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            () => tab.Button("Content.Form.Save").ClickAsync(),
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
