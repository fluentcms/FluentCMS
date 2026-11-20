using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The list of articles of A-12's acceptance for the built-in Editor on the Blog sample: the chosen columns, kept across a
// reload, two sort keys, a page with a total, and a row found and opened by keyboard alone.
public sealed class ListTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorChoosesColumnsSortsByTwoKeysPagesAndOpensAnArticleByKeyboard()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/article?pageSize=3");

        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(3);
        await Expect(Headers(grid)).ToHaveTextAsync([Starts("Title"), Starts(Text("Content.List.Column.Status")), Starts(Text("Content.List.Column.UpdatedAt"))]);
        // The host is shared, so other cases may have added articles: the expectations follow the total the list shows, of the
        // Blog sample's four at least.
        var total = await TotalAsync(tab);
        total.ShouldBeGreaterThanOrEqualTo(4);
        await Expect(tab.Page.Locator(".content-list-pager")).ToContainTextAsync(Text("Content.List.PageOf", "1", $"{(total + 2) / 3}"));

        // The columns: Category is added and the status taken out; the read names only what is shown.
        await tab.Button("Content.List.Columns").ClickAsync();
        await ColumnCheckbox(tab, "Category").ClickAsync();
        await ColumnCheckbox(tab, Text("Content.List.Column.Status")).ClickAsync();
        var read = await ListReadOfAsync(tab, () => tab.Button("Content.List.ColumnsApply").ClickAsync(), "fields=title,updatedAt,category&");
        await Expect(Headers(grid)).ToHaveTextAsync([Starts("Title"), Starts(Text("Content.List.Column.UpdatedAt")), Starts("Category")]);
        read.ShouldContain("withTotal=true");

        // Two sort keys: Category, then Title descending with Shift.
        // Each click waits for the grid to show the sort before the next, which builds on it.
        await SortButton(grid, "Category").ClickAsync();
        await Expect(grid.Locator("th[aria-sort=ascending]")).ToHaveTextAsync(new Regex("^Category"));
        await SortButton(grid, "Title").ClickAsync(new LocatorClickOptions { Modifiers = [KeyboardModifier.Shift] });
        await Expect(SortButton(grid, "Title")).ToContainTextAsync(Text("Components.GridSortLevel", "2", "2"));
        // The second toggle of Title is made by keyboard: Shift with Enter on its header.
        read = await ListReadOfAsync(tab, () => Ui.PressAsync(SortButton(grid, "Title"), "Shift+Enter"), "sort=category,-title&");
        await Expect(tab.Page).ToHaveURLAsync(new Regex("sort=category%2C-title"));
        read.ShouldStartWith("?sort=category,-title&page=1&pageSize=3&");
        await Expect(grid.Locator("th[aria-sort=ascending]")).ToHaveTextAsync(new Regex("^Category"));
        await Expect(tab.Announced("Sorted by Category, ascending, then by Title, descending")).ToBeAttachedAsync();

        // The second page starts at the fourth article.
        await tab.Button("Content.List.Next").ClickAsync();
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(Math.Min(3, total - 3));
        await Expect(grid.Locator("tbody tr").First).ToHaveAttributeAsync("aria-rowindex", "5");
        await tab.Button("Content.List.Previous").ClickAsync();
        await Expect(grid.Locator("tbody tr")).ToHaveCountAsync(3);

        // The choice is kept in the browser: the list without columns in its address shows them after a reload.
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article").AbsoluteUri);
        await Expect(Headers(grid)).ToHaveTextAsync([Starts("Title"), Starts(Text("Content.List.Column.UpdatedAt")), Starts("Category")]);
        await tab.ReloadAsync();
        await Expect(Headers(grid)).ToHaveTextAsync([Starts("Title"), Starts(Text("Content.List.Column.UpdatedAt")), Starts("Category")]);

        // By keyboard alone: a row's checkbox, then its button that opens it in a side panel (A-15), and the next row's checkbox,
        // which the arrows also move to; Space selects and Enter opens.
        var rows = grid.Locator("tbody tr");
        await Ui.PressAsync(rows.Nth(0).Locator("input[type=checkbox]"), "Tab");
        await tab.Page.WaitForFunctionAsync("() => document.activeElement?.closest('tbody tr td:last-child fluent-button') != null", null, new PageWaitForFunctionOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        await tab.Page.Keyboard.PressAsync("Tab");
        await Expect(rows.Nth(1).Locator("input[type=checkbox]")).ToBeFocusedAsync();
        await tab.Page.Keyboard.PressAsync("ArrowDown");
        await Expect(rows.Nth(2).Locator("input[type=checkbox]")).ToBeFocusedAsync();
        await tab.Page.Keyboard.PressAsync("ArrowUp");
        await Expect(rows.Nth(1).Locator("input[type=checkbox]")).ToBeFocusedAsync();
        await tab.Page.Keyboard.PressAsync("Space");
        await Expect(rows.Nth(1)).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(tab.Announced(Text("Components.GridRowsSelected", "1"))).ToBeAttachedAsync();
        await Expect(tab.Page.Locator(".content-list-selection")).ToContainTextAsync(Text("Content.List.PageSelected", "1"));

        var title = await rows.Nth(1).Locator("a").TextContentAsync();
        var href = await rows.Nth(1).Locator("a").GetAttributeAsync("href") ?? "";
        await tab.Page.Keyboard.PressAsync("Enter");
        // The address names the content locale of the list (A-17).
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, href + "?locale=en").AbsoluteUri);
        title.ShouldNotBeNullOrWhiteSpace();
        href.ShouldMatch("^content/article/[0-9a-f-]{36}$");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Under 1024 pixels the sidebar is a drawer, which gives focus back to its opener when it closes; a navigation from it
    // still ends on the new page's heading (#3178), and so does one from a type to another.
    [Fact]
    public async Task ANavigationFromTheDrawerOrToAnotherTypeEndsOnThePageHeading()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.SetViewportSizeAsync(800, 900);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());

        await tab.Page.Locator(".shell-open-navigation fluent-button").ClickAsync();
        var drawer = tab.Page.GetByRole(AriaRole.Navigation, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.Sidebar.Label"), Exact = true }).Last;
        await drawer.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true }).ClickAsync();

        await Expect(tab.Page.Locator("main h1")).ToHaveTextAsync("Article");
        await Expect(tab.Page.Locator("main h1")).ToBeFocusedAsync();

        await tab.Page.SetViewportSizeAsync(1280, 900);
        await tab.Sidebar.First.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Author", Exact = true }).ClickAsync();

        await Expect(tab.Page.Locator("main h1")).ToHaveTextAsync("Author");
        await Expect(tab.Page.Locator("main h1")).ToBeFocusedAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The column headers but the selection's; a sorted one also holds its mark and, with several keys, its level.
    // The columns of the entry, without the last one of the row's actions.
    private static ILocator Headers(ILocator grid) => grid.Locator("thead th:not(.cms-data-grid-select):not(:last-child)");

    // The number of entries the pager shows, from the resource's text around it.
    private static async Task<int> TotalAsync(AdminTab tab)
    {
        var pattern = "^" + Regex.Escape(Text("Content.List.Total")).Replace(@"\{0}", @"([0-9,.\s]+)", StringComparison.Ordinal) + "$";
        var shown = tab.Page.Locator(".content-list-pager > span").First;
        await Expect(shown).ToHaveTextAsync(new Regex(pattern));
        var text = Regex.Match((await shown.TextContentAsync())!.Trim(), pattern).Groups[1].Value;
        return int.Parse(new string([.. text.Where(char.IsDigit)]), System.Globalization.CultureInfo.InvariantCulture);
    }

    // A checkbox of the columns dialog: the wrapped set puts its label beside it, in the field that holds both.
    private static ILocator ColumnCheckbox(AdminTab tab, string label) =>
        tab.Page.Locator(".content-list-columns fluent-field").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($@"^\s*{Regex.Escape(label)}\s*$") }).Locator("fluent-checkbox");

    private static ILocator SortButton(ILocator grid, string title) =>
        grid.Locator("thead th button").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^{Regex.Escape(title)}") });

    // A sorted header also holds its mark, such as "Updated↓", so a header is matched by how it starts.
    private static Regex Starts(string text) => new($"^\\s*{Regex.Escape(text)}");

    // Runs the action and returns the query string of the read of the list it caused, unescaped, once one has that part.
    private static async Task<string> ListReadOfAsync(AdminTab tab, Func<Task> action, string part)
    {
        var request = await tab.Page.RunAndWaitForRequestAsync(
            action,
            request => request.Url.StartsWith(tab.ManagementUrl("content/article?"), StringComparison.Ordinal) && Query(request).Contains(part, StringComparison.Ordinal),
            new PageRunAndWaitForRequestOptions { Timeout = (float)AdminBrowser.Wait.TotalMilliseconds });
        return Query(request);
    }

    private static string Query(IRequest request) => Uri.UnescapeDataString(new Uri(request.Url).Query);

    // A text of the admin's resources with its arguments, or of the wrapped set's when its key starts with "Components.".
    private static string Text(string key, params string[] arguments)
    {
        var text = key.StartsWith("Components.", StringComparison.Ordinal) ? Repository.ComponentText(key["Components.".Length..]) : Repository.AdminText(key);
        return Ui.Format(text, arguments);
    }
}
