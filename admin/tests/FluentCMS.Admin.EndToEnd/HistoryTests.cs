using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// Back while a page starts lands on the entry before it (#3395). While a location-changing handler was registered, as the
// shell's drawer and every form registered one from their start, Blazor took each Back back, asked the handlers and went
// again, so a second Back in between went from the page being left and landed on another entry.
public sealed class HistoryTests(EndToEndHost host) : BrowserTest(host)
{
    private static readonly Regex Opinion = new(@"content/article\?locale=en&filter\[category\]\[eq\]=opinion$");

    private static readonly Regex Welcome = new(@"content/article\?locale=en&filter\[title\]\[startsWith\]=Welcome$");

    private static readonly string[] Titles = ["Welcome to FluentCMS", "Typed content, no magic"];

    [Fact]
    public async Task TwoBacksFromAnEntryThatIsStillStartingLandTwoEntriesBack()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "content/article");
        var grid = tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true });
        await Expect(grid.Locator("tbody tr").First).ToBeVisibleAsync();
        await ListAsync(tab, "content/article?locale=en&filter[category][eq]=opinion", Opinion, "Typed content, no magic");
        await ListAsync(tab, "content/article?locale=en&filter[title][startsWith]=Welcome", Welcome, "Welcome to FluentCMS");

        // The entry's read is held, so the entry page has not finished starting when Back is pressed.
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await tab.Page.RouteAsync(new Regex(@"/api/management/v1/content/article/[0-9a-f-]{36}"), async route =>
        {
            asked.TrySetResult();
            await release.Task;
            try
            {
                await route.ContinueAsync();
            }
            catch (PlaywrightException)
            {
            }
        });
        await Row(grid, "Welcome to FluentCMS").GetByRole(AriaRole.Link).ClickAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex(@"content/article/[0-9a-f-]{36}\?locale=en$"));
        await asked.Task.WaitAsync(AdminBrowser.Wait, TestContext.Current.CancellationToken);

        // The second Back comes as the page sees the first, as a quick second press does, while the list it reached starts.
        await tab.Page.EvaluateAsync("() => { addEventListener('popstate', () => history.back(), { once: true }); history.back(); }");

        await Expect(tab.Page).ToHaveURLAsync(Opinion);
        await ExpectRowsAsync(grid, "Typed content, no magic");
        await tab.Page.UnrouteAllAsync(new PageUnrouteAllOptions { Behavior = UnrouteBehavior.IgnoreErrors });
        release.SetResult();

        // Forward goes to the list after it, so Back left the history on the entry it shows.
        await tab.Page.GoForwardAsync();
        await Expect(tab.Page).ToHaveURLAsync(Welcome);
        await ExpectRowsAsync(grid, "Welcome to FluentCMS");
    }

    private static async Task ListAsync(AdminTab tab, string route, Regex address, string shown)
    {
        await tab.Page.EvaluateAsync($"() => Blazor.navigateTo('{route}')");
        await Expect(tab.Page).ToHaveURLAsync(address);
        await ExpectRowsAsync(tab.Page.GetByRole(AriaRole.Grid, new PageGetByRoleOptions { Name = "Article", Exact = true }), shown);
    }

    // The list has read its rows: the article named is shown and no other article of the Blog sample's that a filter here
    // leaves out.
    private static async Task ExpectRowsAsync(ILocator grid, string shown)
    {
        await Expect(grid.Locator("[aria-busy]")).ToHaveCountAsync(0);
        await Expect(Row(grid, shown)).ToHaveCountAsync(1);
        foreach (var title in Titles.Where(title => title != shown))
        {
            await Expect(Row(grid, title)).ToHaveCountAsync(0);
        }
    }

    private static ILocator Row(ILocator grid, string title) =>
        grid.Locator("tbody tr").Filter(new LocatorFilterOptions { Has = grid.Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = title, Exact = true }) });
}
