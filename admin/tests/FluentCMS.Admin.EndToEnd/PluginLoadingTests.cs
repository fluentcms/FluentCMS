using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The media half is the reference host's one admin half: its assembly is fetched on the first visit of /media and never in
// the second site, where the host disables the media plugin (spec 2.6, row 8.15).
public sealed class PluginLoadingTests(EndToEndHost host) : BrowserTest(host)
{
    // The half's assembly as the publish names it, with the fingerprint, and none of the files beside it.
    private static readonly Regex MediaHalf = new(@"/_framework/FluentCMS\.Plugins\.Media\.Admin(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    [Fact]
    public async Task TheMediaHalfIsNotInTheFirstDownloadAndIsFetchedOnceOnTheFirstVisitOfMedia()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var fetched = HalfRequests(tab);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[0].Id);

        var media = tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Media", Exact = true });
        await Expect(media).ToBeVisibleAsync();
        fetched.ShouldBeEmpty();

        await media.ClickAsync();
        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        fetched.Count.ShouldBe(1);

        await tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Shell.Sidebar.Home"), Exact = true }).ClickAsync();
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        await media.ClickAsync();
        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        fetched.Count.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task InTheSiteWhereMediaIsDisabledItHasNoMenuItemItsPageIsNotFoundAndItsHalfIsNeverFetched()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var fetched = HalfRequests(tab);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await tab.SwitchSiteAsync(Host.Sites[1]);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[1].Id);
        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true })).ToBeVisibleAsync();

        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Media", Exact = true })).ToHaveCountAsync(0);

        // The tab keeps the second site across the load of /media.
        await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, "media").AbsoluteUri));
        await Expect(tab.Heading("Shell.NotFound.Heading")).ToBeVisibleAsync();
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[1].Id);
        fetched.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static ILocator MediaHeading(AdminTab tab) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = "Media", Exact = true });

    private static ConcurrentQueue<string> HalfRequests(AdminTab tab)
    {
        var fetched = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (MediaHalf.IsMatch(new Uri(request.Url).AbsolutePath))
            {
                fetched.Enqueue(request.Url);
            }
        };
        return fetched;
    }
}
