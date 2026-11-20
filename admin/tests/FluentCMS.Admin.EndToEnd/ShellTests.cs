using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The shell of A1's exit criteria for the built-in Editor on the Blog sample: the sidebar of the types it may read, in each
// of its sites, and the switch between them. The content links lead to the not-found page until A2's lists, so the links and
// their names are checked, not a page behind them.
public sealed class ShellTests(EndToEndHost host) : BrowserTest(host)
{
    // The Blog sample's collections and the host's memo of BulkTests and landing of BlockTests, which name no display name, so
    // the shell shows the name with a capital first letter.
    private static readonly string[] BlogTypes = ["article", "author", "tag", "memo", "landing"];

    [Fact]
    public async Task TheSidebarListsTheTypesTheEditorMayReadAndNoAdministration()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "");

        await SidebarShouldListTheBlogTypesAsync(tab);
        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Shell.Sidebar.Home"), Exact = true }))
            .ToHaveAttributeAsync("aria-current", "page");
        await Expect(tab.Sidebar.GetByText(Repository.AdminText("Shell.Sidebar.Administration"), new LocatorGetByTextOptions { Exact = true })).ToHaveCountAsync(0);
        await Expect(tab.Sidebar.GetByText(Repository.AdminText("Shell.Sidebar.Settings"), new LocatorGetByTextOptions { Exact = true })).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheEditorSwitchesSiteAndTheTabKeepsTheChosenSite()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "");
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[0].Id);

        // The reads of the switch name the chosen site, and the switch is announced.
        var second = Host.Sites[1];
        var access = await tab.SwitchSiteAsync(second);
        access.Status.ShouldBe(200);
        (await access.Request.HeaderValueAsync("x-cms-site")).ShouldBe(second.Id, StringCompareShould.IgnoreCase);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(second.Id);
        await SidebarShouldListTheBlogTypesAsync(tab);

        // The tab keeps the site across a reload, whose refresh token, if rotated just before, is taken within the server's
        // 30-second reuse leeway.
        var schema = tab.NextAnswerAsync(response => response.Url == tab.ManagementUrl("schema"));
        await tab.ReloadAsync();
        (await (await schema).Request.HeaderValueAsync("x-cms-site")).ShouldBe(second.Id, StringCompareShould.IgnoreCase);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(second.Id);
        await SidebarShouldListTheBlogTypesAsync(tab);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static async Task SidebarShouldListTheBlogTypesAsync(AdminTab tab)
    {
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        await Expect(tab.Sidebar.Locator("a[href*='content/']")).ToHaveCountAsync(BlogTypes.Length);
        foreach (var type in BlogTypes)
        {
            var name = char.ToUpperInvariant(type[0]) + type[1..];
            await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = name }))
                .ToHaveAttributeAsync("href", new Regex($"(^|/)content/{type}$"));
        }
    }
}
