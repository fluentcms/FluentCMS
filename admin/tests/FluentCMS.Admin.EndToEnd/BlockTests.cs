using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The block editor of A-26 for the built-in Editor, on the host's landing type: a band holds cards, and a card a link, so a
// link is at depth 3. Blocks are added, ordered, collapsed, duplicated and removed by keyboard alone, and once by drag; the
// save sends each block with an id of its own, and a read of the entry gives them back unchanged.
public sealed partial class BlockTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorBuildsOrdersAndSavesBlocksToDepthThree()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/landing/new").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await tab.Page.Locator("[data-field='title'] input").FillAsync("Blocks in the browser");

        // A band with a card and its link, then a note; focus goes to each new block's title.
        await AddAsync(tab, "band", "Band");
        await Expect(Toggle(Item(tab, 0))).ToBeFocusedAsync();
        await Item(tab, 0).Locator("input").First.FillAsync("Team");
        await PressAsync(Item(tab, 0).Locator("fluent-button").Filter(Exactly("Content.Nested.Add", "Card")), "Enter");
        var card = Item(tab, 0).Locator(".nested-items > li").First;
        await Expect(Toggle(card)).ToBeFocusedAsync();
        await card.Locator("input").Nth(0).FillAsync("Ada");
        await card.Locator("input").Nth(1).FillAsync("Profile");
        await AddAsync(tab, "note", "Note");
        await Item(tab, 1).Locator("input").FillAsync("Hello");

        // Alt with an arrow moves the note up, and focus stays on its title.
        await Toggle(Item(tab, 1)).FocusAsync();
        await tab.Page.Keyboard.PressAsync("Alt+ArrowUp");
        await Expect(Titles(tab)).ToHaveTextAsync([Text("Content.Nested.Item", "Note", "1"), Text("Content.Nested.Item", "Band", "2")]);
        await Expect(Toggle(Item(tab, 0))).ToBeFocusedAsync();

        // The band collapses to its first text, is duplicated, and the note is removed, each by its control and Enter.
        await PressAsync(Toggle(Item(tab, 1)), "Enter");
        await Expect(Toggle(Item(tab, 1))).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Item(tab, 1).Locator(".nested-preview")).ToHaveTextAsync("Team");
        await PressAsync(Action(tab, "Content.Nested.Duplicate", "Band", "2"), "Enter");
        await Expect(Titles(tab)).ToHaveCountAsync(3);
        await Expect(Toggle(Item(tab, 2))).ToBeFocusedAsync();
        await Expect(Toggle(Item(tab, 2))).ToHaveAttributeAsync("aria-expanded", "true");
        await PressAsync(Action(tab, "Content.Nested.Remove", "Note", "1"), "Enter");
        await Expect(Titles(tab)).ToHaveTextAsync([Text("Content.Nested.Item", "Band", "1"), Text("Content.Nested.Item", "Band", "2")]);
        await Expect(Toggle(Item(tab, 0))).ToBeFocusedAsync();

        // The copy is dragged above the band, and the band stays collapsed.
        await Item(tab, 1).Locator(":scope > .nested-head").DragToAsync(Item(tab, 0));
        await Expect(Toggle(Item(tab, 0))).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Toggle(Item(tab, 1))).ToHaveAttributeAsync("aria-expanded", "false");

        var created = await AnswerAsync(tab, () => tab.Page.Keyboard.PressAsync("Control+s"), "POST", tab.ManagementUrl("content/landing"));
        created.Status.ShouldBe(201);
        var sent = JsonNode.Parse(created.Request.PostData!)!["sections"]!.AsArray();
        sent.Select(block => (string)block!["type"]!).ShouldBe(["band", "band"]);
        string[] ids = [.. sent.SelectMany(block => new[] { block!["id"], block["cards"]![0]!["id"] }).Select(id => (string)id!)];
        ids.ShouldAllBe(id => IdPattern().IsMatch(id));
        ids.Distinct().Count().ShouldBe(4);
        foreach (var band in sent)
        {
            band!["title"]!.GetValue<string>().ShouldBe("Team");
            band["cards"]![0]!["link"]!["label"]!.GetValue<string>().ShouldBe("Profile");
        }

        // Read again, the entry has the blocks as they were sent, with their ids, and a save sends them back unchanged.
        await Expect(tab.Page).ToHaveURLAsync(new Regex("content/landing/[0-9a-f-]{36}"));
        await tab.ReloadAsync();
        await Expect(Titles(tab)).ToHaveCountAsync(2);
        await tab.Page.Locator("[data-field='title'] input").FocusAsync();
        var location = (await created.HeaderValueAsync("location")).ShouldNotBeNull();
        var id = location[(location.LastIndexOf('/') + 1)..].Split('?')[0];
        var saved = await AnswerAsync(tab, () => tab.Page.Keyboard.PressAsync("Control+s"), "PUT", tab.ManagementUrl($"content/landing/{id}"));
        saved.Status.ShouldBe(200);
        JsonNode.DeepEquals(JsonNode.Parse(saved.Request.PostData!)!["sections"], sent).ShouldBeTrue();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{12}$")]
    private static partial Regex IdPattern();

    private static string Text(string key, params string[] arguments)
    {
        var text = Repository.AdminText(key);
        for (var index = 0; index < arguments.Length; index++)
        {
            text = text.Replace("{" + index + "}", arguments[index], StringComparison.Ordinal);
        }

        return text;
    }

    private static LocatorFilterOptions Exactly(string key, params string[] arguments) =>
        new() { HasTextRegex = new Regex($@"^\s*{Regex.Escape(Text(key, arguments))}\s*$") };

    private static ILocator Field(AdminTab tab) => tab.Page.Locator("[data-field='sections'] > fieldset");

    private static ILocator Item(AdminTab tab, int position) => Field(tab).Locator(":scope > ul > li").Nth(position);

    private static ILocator Titles(AdminTab tab) => Field(tab).Locator(":scope > ul > li > .nested-head .nested-toggle > span:first-child");

    private static ILocator Toggle(ILocator item) => item.Locator(":scope > .nested-head .nested-toggle");

    private static ILocator Action(AdminTab tab, string key, string type, string position) =>
        Field(tab).Locator($":scope > ul > li > .nested-head fluent-button[aria-label=\"{Text(key, Text("Content.Nested.Item", type, position))}\"]");

    // The type is chosen, and the add button pressed, from the keyboard.
    private static async Task AddAsync(AdminTab tab, string type, string label)
    {
        await Field(tab).Locator(":scope > .nested-actions select").SelectOptionAsync(type);
        await PressAsync(Field(tab).Locator(":scope > .nested-actions fluent-button").Filter(Exactly("Content.Nested.Add", label)), "Enter");
    }

    private static async Task PressAsync(ILocator control, string key)
    {
        await control.FocusAsync();
        await control.PressAsync(key);
    }

    private static Task<IResponse> AnswerAsync(AdminTab tab, Func<Task> action, string method, string url) =>
        tab.Page.RunAndWaitForResponseAsync(
            action,
            response => response.Request.Method == method && response.Url == url,
            new PageRunAndWaitForResponseOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
}
