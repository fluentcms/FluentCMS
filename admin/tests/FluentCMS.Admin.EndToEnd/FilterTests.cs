using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The list of articles of A-48's acceptance for the built-in Editor on the Blog sample: an article found by search and by a
// filter on a text, a date and a select field, the filters kept across a reload and Back, and the badge of each locale.
public sealed class FilterTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TheEditorFindsAnArticleBySearchAndByFiltersOnATextADateAndASelectField()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(new Uri(Host.Admin, "content/article").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        await Expect(grid.Locator("tbody tr").First).ToBeVisibleAsync();

        // The badges: the list reads every locale, and an article published in English only has an empty French badge.
        var typed = Row(grid, "Typed content, no magic");
        await Expect(typed.Locator(".content-badge-published")).ToHaveTextAsync(new Regex(@"^\s*en\s+Published\s*$", RegexOptions.IgnoreCase));
        await Expect(typed.Locator(".content-badge-missing")).ToContainTextAsync("fr");
        await Expect(Row(grid, "Welcome to FluentCMS").Locator(".content-badge-published")).ToHaveCountAsync(2);
        await Expect(Row(grid, "FluentCMS 1.0 release notes").Locator(".content-badge-schedule")).ToContainTextAsync(Text("Content.Badge.PublishAt"));

        // Search: the index is written a moment after the seed, so the search is made again until it has the article.
        var search = tab.Page.GetByRole(AriaRole.Searchbox, new PageGetByRoleOptions { Name = Text("Content.Search.Label"), Exact = true });
        await search.FillAsync("tour");
        await search.PressAsync("Enter");
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"content/article\?locale=en&q=tour$"));
        for (var attempt = 0; ; attempt++)
        {
            await Expect(tab.Page.Locator("table[aria-busy]")).ToHaveCountAsync(0);
            if (attempt == 20 || (await Row(grid, "A tour of the blog sample").CountAsync() == 1 && await Row(grid, "Welcome to FluentCMS").CountAsync() == 0))
            {
                break;
            }

            await tab.Page.WaitForTimeoutAsync(500);
            await tab.ReloadAsync();
        }

        await ExpectSeedAsync(grid, "A tour of the blog sample");
        await search.FillAsync("");
        await search.PressAsync("Enter");
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"content/article\?locale=en$"));

        // A text field.
        await AddFilterAsync(tab, "Title", "startsWith", () => tab.Page.GetByLabel(Text("Content.Filter.Value"), new PageGetByLabelOptions { Exact = true }).FillAsync("Typed"));
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"\?locale=en&filter\[title\]\[startsWith\]=Typed$"));
        await ExpectSeedAsync(grid, "Typed content, no magic");

        // A select field, beside the text filter, then the text filter removed.
        await AddFilterAsync(tab, "Category", "eq", () => tab.Page.GetByLabel(Text("Content.Filter.Value"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync("opinion"));
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"\?locale=en&filter\[title\]\[startsWith\]=Typed&filter\[category\]\[eq\]=opinion$"));
        await ExpectSeedAsync(grid, "Typed content, no magic");
        await tab.Page.Locator($"fluent-button[aria-label='{Text("Content.Filter.Remove", "Title starts with Typed")}']").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"\?locale=en&filter\[category\]\[eq\]=opinion$"));
        await ExpectSeedAsync(grid, "Typed content, no magic");
        await tab.Page.Locator($"fluent-button[aria-label='{Text("Content.Filter.Remove", "Category is opinion")}']").ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"content/article\?locale=en$"));

        // A date field, which a reload keeps.
        await AddFilterAsync(tab, "Published on", "gt", () => tab.Page.GetByLabel(Text("Content.Filter.Value"), new PageGetByLabelOptions { Exact = true }).FillAsync("2000-01-01"));
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"\?locale=en&filter\[publishedOn\]\[gt\]=(2000-01-01|2000-01-02)T[0-9]{2}%3A[0-9]{2}%3A59\.9999999Z$"));
        await ExpectSeedAsync(grid, "Welcome to FluentCMS");
        await tab.ReloadAsync();
        await ExpectSeedAsync(grid, "Welcome to FluentCMS");
        await Expect(tab.Page.Locator(".content-filter")).ToHaveCountAsync(1);

        // Back goes through the addresses before: no filter, then the select filter alone.
        await tab.Page.GoBackAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"content/article\?locale=en$"));
        await tab.Page.GoBackAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"\?locale=en&filter\[category\]\[eq\]=opinion$"));
        await ExpectSeedAsync(grid, "Typed content, no magic");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static async Task AddFilterAsync(AdminTab tab, string field, string filterOperator, Func<Task> enterValue)
    {
        await tab.Button("Content.Filter.Add").ClickAsync();
        await tab.Page.GetByLabel(Text("Content.Filter.Field"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = field });
        await tab.Page.GetByLabel(Text("Content.Filter.Operator"), new PageGetByLabelOptions { Exact = true }).SelectOptionAsync(filterOperator);
        await enterValue();
        await tab.Button("Content.Filter.Apply").ClickAsync();
    }

    private static ILocator Row(ILocator grid, string title) =>
        grid.Locator("tbody tr").Filter(new LocatorFilterOptions { Has = grid.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = title, Exact = true }) });

    // The host is shared, and other cases add articles of their own, so a list is checked against the Blog sample's English
    // articles: those named are shown, and the others are not.
    private static readonly string[] SeedTitles =
        ["Welcome to FluentCMS", "Typed content, no magic", "A tour of the blog sample", "A post we took down", "FluentCMS 1.0 release notes"];

    private static async Task ExpectSeedAsync(ILocator grid, params string[] shown)
    {
        foreach (var title in SeedTitles)
        {
            await Expect(Row(grid, title)).ToHaveCountAsync(shown.Contains(title) ? 1 : 0);
        }
    }

    private static string Text(string key, params string[] arguments)
    {
        var text = Repository.AdminText(key);
        for (var index = 0; index < arguments.Length; index++)
        {
            text = text.Replace($"{{{index}}}", arguments[index], StringComparison.Ordinal);
        }

        return text;
    }
}
