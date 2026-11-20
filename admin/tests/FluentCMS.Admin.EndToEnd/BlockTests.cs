using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The block editor of A-26 for the built-in Editor, on the host's landing type: a band holds cards and links, and a card a
// link, an author and an image, so a link is at depth 3. Blocks are added, ordered, collapsed, duplicated and removed by
// keyboard alone, and once by drag; the save sends each block with an id of its own, and a read of the entry gives them back
// unchanged.
public sealed partial class BlockTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorBuildsOrdersAndSavesBlocksToDepthThree()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/landing/new");
        var title = $"Blocks in the browser {Mark}";
        await tab.Page.Locator("[data-field='title'] input").FillAsync(title);

        // A band with a card and its link, then a note; focus goes to each new block's title.
        await AddAsync(tab, "band", "Band");
        await Expect(Toggle(Item(tab, 0))).ToBeFocusedAsync();
        await Item(tab, 0).Locator("input").First.FillAsync("Team");
        await Ui.PressAsync(Item(tab, 0).Locator("fluent-button").Filter(Exactly("Content.Nested.Add", "Card")), "Enter");
        var card = Item(tab, 0).Locator(".nested-items > li").First;
        await Expect(Toggle(card)).ToBeFocusedAsync();
        await card.Locator("input").Nth(0).FillAsync("Ada");
        await card.Locator("input").Nth(1).FillAsync("Profile");

        // The relation and the media editors work in a block as at the top level.
        await Expect(card.Locator(".relation-field-actions fluent-button").Filter(Exactly("Content.Relation.ChooseEntry"))).ToBeVisibleAsync();
        await Expect(card.Locator(".media-field")).ToBeVisibleAsync();

        // A link of the band, collapsed, stays collapsed while the band's title is typed in.
        var links = Item(tab, 0).Locator(":scope > .nested-body > .block-form > div:nth-of-type(3) > fieldset");
        await Ui.PressAsync(links.Locator(":scope > .nested-actions fluent-button").Filter(Exactly("Content.Nested.Add", "Link")), "Enter");
        await links.Locator(":scope > ul > li input").First.FillAsync("Home");
        await Ui.PressAsync(Toggle(links.Locator(":scope > ul > li")), "Enter");
        await Expect(Toggle(links.Locator(":scope > ul > li"))).ToHaveAttributeAsync("aria-expanded", "false");
        await Item(tab, 0).Locator("input").First.FillAsync("Team and friends");
        await Expect(Item(tab, 0).Locator("input").First).ToHaveValueAsync("Team and friends");
        await Expect(Toggle(links.Locator(":scope > ul > li"))).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(links.Locator(".nested-preview")).ToHaveTextAsync("Home");
        await AddAsync(tab, "note", "Note");
        await Item(tab, 1).Locator("input").FillAsync("Hello");

        // Alt with an arrow moves the note up, and focus stays on its title.
        await Ui.PressAsync(Toggle(Item(tab, 1)), "Alt+ArrowUp");
        await Expect(Titles(tab)).ToHaveTextAsync([Text("Content.Nested.Item", "Note", "1"), Text("Content.Nested.Item", "Band", "2")]);
        await Expect(Toggle(Item(tab, 0))).ToBeFocusedAsync();

        // The band collapses to its first text, is duplicated, and the note is removed, each by its control and Enter.
        await Ui.PressAsync(Toggle(Item(tab, 1)), "Enter");
        await Expect(Toggle(Item(tab, 1))).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Toggle(Item(tab, 1)).Locator(".nested-preview")).ToHaveTextAsync("Team and friends");
        await Ui.PressAsync(Action(tab, "Content.Nested.Duplicate", "Band", "2"), "Enter");
        await Expect(Titles(tab)).ToHaveCountAsync(3);
        await Expect(Toggle(Item(tab, 2))).ToBeFocusedAsync();
        await Expect(Toggle(Item(tab, 2))).ToHaveAttributeAsync("aria-expanded", "true");
        await Ui.PressAsync(Action(tab, "Content.Nested.Remove", "Note", "1"), "Enter");
        await Expect(Titles(tab)).ToHaveTextAsync([Text("Content.Nested.Item", "Band", "1"), Text("Content.Nested.Item", "Band", "2")]);
        await Expect(Toggle(Item(tab, 0))).ToBeFocusedAsync();

        // The copy is dragged above the band, and the band stays collapsed; the assertions wait for the drop to land.
        await Ui.DragAsync(Item(tab, 1).Locator(":scope > .nested-head"), Item(tab, 0), "nested-drop");
        await Expect(Toggle(Item(tab, 0))).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Toggle(Item(tab, 1))).ToHaveAttributeAsync("aria-expanded", "false");

        var created = await tab.AnswerAsync(() => tab.Page.Keyboard.PressAsync("Control+s"), "POST", tab.ManagementUrl("content/landing"));
        created.Status.ShouldBe(201);
        var sent = JsonNode.Parse(created.Request.PostData!)!["sections"]!.AsArray();
        sent.Select(block => (string)block!["type"]!).ShouldBe(["band", "band"]);
        string[] ids = [.. sent.SelectMany(block => new[] { block!["id"], block["cards"]![0]!["id"] }).Select(id => (string)id!)];
        ids.ShouldAllBe(id => IdPattern().IsMatch(id));
        ids.Distinct().Count().ShouldBe(4);
        foreach (var band in sent)
        {
            band!["title"]!.GetValue<string>().ShouldBe("Team and friends");
            band["cards"]![0]!["link"]!["label"]!.GetValue<string>().ShouldBe("Profile");
            band["links"]!.ToJsonString().ShouldBe("""[{"label":"Home"}]""");
        }

        // Read again, the entry has the blocks as they were sent, with their ids, and a save sends them back unchanged.
        await Expect(tab.Page).ToHaveURLAsync(new Regex("content/landing/[0-9a-f-]{36}"));
        await tab.ReloadAsync();
        await Expect(tab.Page.Locator("[data-field='title'] input")).ToHaveValueAsync(title);
        await Expect(Titles(tab)).ToHaveCountAsync(2);
        await Expect(Item(tab, 0).Locator(".media-field")).ToBeVisibleAsync();
        await Expect(Item(tab, 0).Locator(".relation-field")).ToBeVisibleAsync();
        var id = await Ui.CreatedIdAsync(created, "content/landing");
        var saved = await tab.AnswerAsync(() => Ui.PressAsync(tab.Page.Locator("[data-field='title'] input"), "Control+s"), "PUT", tab.ManagementUrl($"content/landing/{id:D}"));
        saved.Status.ShouldBe(200);
        JsonNode.DeepEquals(JsonNode.Parse(saved.Request.PostData!)!["sections"], sent).ShouldBeTrue();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{12}$")]
    private static partial Regex IdPattern();

    private static string Text(string key, params string[] arguments) => Ui.Format(Repository.AdminText(key), arguments);

    private static LocatorFilterOptions Exactly(string key, params string[] arguments) => Ui.Exactly(Text(key, arguments));

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
        await Ui.PressAsync(Field(tab).Locator(":scope > .nested-actions fluent-button").Filter(Exactly("Content.Nested.Add", label)), "Enter");
    }
}
