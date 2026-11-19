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
        var requested = new List<string>();
        tab.Page.Request += (_, request) => requested.Add(request.Url);
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

    private static ILocator ToolbarButton(AdminTab tab, string resourceKey) =>
        tab.Page.Locator($"[role=toolbar] button[aria-label='{Repository.RichTextText(resourceKey)}']");

    private static Task<IResponse> SaveAsync(AdminTab tab, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            () => tab.Button("Content.Form.Save").ClickAsync(),
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
