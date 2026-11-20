using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The relation picker of A-15 for the built-in Editor on the Blog sample: the author of a new article chosen by search, its
// tags chosen and ordered by keys, by drag and by a button, an author created in a side panel and chosen without losing the
// article's changes, and a related entry opened in a side panel that gives focus back to the control that opened it.
public sealed class RelationTests(EndToEndHost host) : BrowserTest(host)
{
    private const string AuthorPicker = "Choose an entry: Author";
    private const string TagPicker = "Choose entries: Tag";

    [Fact]
    public async Task TheEditorSetsTheAuthorAndTheOrderedTagsOfAnArticle()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/article/new");
        var article = $"Related in the browser {Mark}";
        var author = $"Hedy Lamarr {Mark}";
        var title = tab.Page.Locator("[data-field='title'] input");
        await title.FillAsync(article);

        // The author, by search: the index may still be filling, so the search is sent again until it finds her. The picker's
        // own first read may still be under way, so the answer waited for is the one that names the search.
        var picker = await tab.OpenDialogAsync(() => FieldButton(tab, "author", "Content.Relation.ChooseEntry").ClickAsync(), AuthorPicker);
        await Ui.UntilAsync(
            async () =>
            {
                var search = await tab.AnswerAsync(() => SearchAsync(picker, "Grace"), response => response.Url.Contains("/content/author?", StringComparison.Ordinal) && response.Url.Contains("q=Grace", StringComparison.Ordinal));
                search.Status.ShouldBe(200);
                await Expect(picker.Locator(".cms-data-grid[aria-busy], .cms-data-grid [aria-busy]")).ToHaveCountAsync(0);
                return await Row(picker, "Grace Hopper").CountAsync() > 0;
            },
            "the search index never found Grace Hopper");
        await Row(picker, "Grace Hopper").Locator("input[type=checkbox]").CheckAsync();
        await DialogButton(picker, "Content.Relation.Choose").ClickAsync();
        await Expect(Labels(tab, "author")).ToHaveTextAsync(["Grace Hopper"]);

        // Two tags, then their order by Alt with an arrow, by drag and by a button.
        var tags = await tab.OpenDialogAsync(() => FieldButton(tab, "tags", "Content.Relation.ChooseEntries").ClickAsync(), TagPicker);
        await Expect(Row(tags, "release notes")).ToBeVisibleAsync();
        await Row(tags, "dotnet").Locator("input[type=checkbox]").CheckAsync();
        await Row(tags, "release notes").Locator("input[type=checkbox]").CheckAsync();
        await DialogButton(tags, "Content.Relation.Choose").ClickAsync();
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["dotnet", "release notes"]);

        // The closed picker gives focus back to the button that opened it twice; focus moved to an entry before the second
        // would be taken back.
        await Ui.FocusReturnedAsync(FieldButton(tab, "tags", "Content.Relation.ChooseEntries"));
        await Ui.PressAsync(Entry(tab, "tags", "release notes"), "Alt+ArrowUp");
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["release notes", "dotnet"]);
        await Expect(Entry(tab, "tags", "release notes")).ToBeFocusedAsync();
        await Item(tab, "tags", "release notes").DragToAsync(Item(tab, "tags", "dotnet"));
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["dotnet", "release notes"]);
        await tab.Page.Locator($"[data-field='tags'] fluent-button[aria-label=\"{Text("Content.Relation.MoveDown", "dotnet")}\"]").ClickAsync();
        await Expect(Labels(tab, "tags")).ToHaveTextAsync(["release notes", "dotnet"]);

        // A new author, made in a side panel and chosen once saved; the article keeps what was typed.
        var create = FieldButton(tab, "author", "Content.Relation.Create", "Author");
        await create.ClickAsync();
        var panel = tab.Page.Locator("fluent-drawer[aria-label=\"Author\"]");
        await Expect(panel.Locator("h2")).ToHaveTextAsync(Text("Content.Form.NewHeading", "Author"));
        await panel.Locator("[data-field='name'] input").FillAsync(author);
        var hedy = await Ui.CreatedIdAsync(await tab.AnswerAsync(() => PanelButton(panel, "Content.Form.Save").ClickAsync(), "POST", tab.ManagementUrl("content/author")), "content/author");
        await Expect(Labels(tab, "author")).ToHaveTextAsync([author]);
        await PanelButton(panel, "Content.Panel.Close").ClickAsync();
        await Expect(tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Author", Exact = true })).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(create);
        await Expect(title).ToHaveValueAsync(article);

        var saved = await tab.AnswerAsync(() => tab.Page.Locator(".content-form-toolbar fluent-button").Filter(Exactly("Content.Form.Save")).First.ClickAsync(), "POST", tab.ManagementUrl("content/article"));
        saved.Status.ShouldBe(201);
        var sent = JsonNode.Parse(saved.Request.PostData!)!;
        sent["title"]!.GetValue<string>().ShouldBe(article);
        sent["author"]!.GetValue<string>().ShouldBe(hedy.ToString("D"));
        sent["tags"]!.AsArray().Count.ShouldBe(2);

        // The related entry opens in a side panel and closes back to the control that opened it.
        await Entry(tab, "author", author).ClickAsync();
        await Expect(panel.Locator("h2")).ToHaveTextAsync(author);
        await tab.Page.Keyboard.PressAsync("Escape");
        await Expect(tab.Page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { Name = "Author", Exact = true })).ToBeHiddenAsync();
        await Ui.FocusReturnedAsync(Entry(tab, "author", author));
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key, string argument) => Repository.AdminText(key).Replace("{0}", argument, StringComparison.Ordinal);

    private static LocatorFilterOptions Exactly(string key, string? argument = null) =>
        Ui.Exactly(argument is null ? Repository.AdminText(key) : Text(key, argument));

    private static ILocator FieldButton(AdminTab tab, string field, string key, string? argument = null) =>
        tab.Page.Locator($"[data-field='{field}'] .relation-field-actions fluent-button").Filter(Exactly(key, argument));

    private static ILocator Labels(AdminTab tab, string field) => tab.Page.Locator($"[data-field='{field}'] .relation-field-name");

    private static ILocator Item(AdminTab tab, string field, string label) =>
        tab.Page.Locator($"[data-field='{field}'] .relation-field-item").Filter(new LocatorFilterOptions { Has = tab.Page.Locator(".relation-field-name").GetByText(label, new LocatorGetByTextOptions { Exact = true }) });

    private static ILocator Entry(AdminTab tab, string field, string label) => Item(tab, field, label).Locator(".relation-field-entry fluent-button");

    private static ILocator DialogButton(ILocator dialog, string key) => dialog.Locator("fluent-button").Filter(Exactly(key));

    private static ILocator PanelButton(ILocator panel, string key) => panel.Locator("fluent-button").Filter(Exactly(key)).Last;

    private static ILocator Row(ILocator picker, string label) =>
        picker.Locator(".cms-data-grid tbody tr").Filter(new LocatorFilterOptions { HasText = label });

    private static async Task SearchAsync(ILocator picker, string text)
    {
        var box = picker.Locator("input[type=search]");
        await box.FillAsync(text);
        await box.PressAsync("Enter");
    }
}
