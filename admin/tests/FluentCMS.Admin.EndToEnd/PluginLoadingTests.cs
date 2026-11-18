using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The media half is the reference host's one admin half: its assembly is not in the first download, it is fetched once, by
// its widget on the home page or by the first visit of /media, and never in the second site, where the host disables the
// media plugin, nor for a user without its permission (spec 2.6, row 8.15). Each case signs in on the component gallery, which
// can never load a half, so that the first fetch is the one the case makes.
public sealed class PluginLoadingTests(EndToEndHost host) : BrowserTest(host)
{
    // The half's assembly as the publish names it, with the fingerprint, and none of the files beside it.
    private static readonly Regex MediaHalf = new(@"/_framework/FluentCMS\.Plugins\.Media\.Admin(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    [Fact]
    public async Task TheMediaHalfIsNotInTheFirstDownloadAndIsFetchedOnceByItsWidgetOnTheHomePage()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var fetched = HalfRequests(tab);
        await SignInOnTheGalleryAsync(browser, tab, Host.Editor);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[0].Id);

        var media = tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Media", Exact = true });
        await Expect(media).ToBeVisibleAsync();
        fetched.ShouldBeEmpty();

        await Home(tab).ClickAsync();
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        await Expect(UploadsHeading(tab)).ToBeVisibleAsync();
        await Expect(tab.Page.Locator("main").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true })).ToBeVisibleAsync();
        fetched.Count.ShouldBe(1);

        await media.ClickAsync();
        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        await Home(tab).ClickAsync();
        await Expect(UploadsHeading(tab)).ToBeVisibleAsync();
        fetched.Count.ShouldBe(1);

        // On a tablet, where the sidebar is a drawer, the tiles take two columns, and nothing is wider than the window.
        await tab.Page.SetViewportSizeAsync(768, 1024);
        await Expect(UploadsHeading(tab)).ToBeVisibleAsync();
        (await tab.Page.EvaluateAsync<int>("() => getComputedStyle(document.querySelector('.home-tiles')).gridTemplateColumns.split(' ').length")).ShouldBe(2);
        (await tab.Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth")).ShouldBeTrue();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task InTheSiteWhereMediaIsDisabledItHasNoMenuItemNoWidgetItsPageIsNotFoundAndItsHalfIsNeverFetched()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var fetched = HalfRequests(tab);
        await SignInOnTheGalleryAsync(browser, tab, Host.Editor);
        await tab.SwitchSiteAsync(Host.Sites[1]);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[1].Id);
        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true })).ToBeVisibleAsync();

        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Media", Exact = true })).ToHaveCountAsync(0);

        await Home(tab).ClickAsync();
        await Expect(tab.Page.Locator("main").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Page.Locator("main .home-tile")).ToHaveCountAsync(1);
        await Expect(UploadsHeading(tab)).ToHaveCountAsync(0);

        // The tab keeps the second site across the load of /media.
        await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(new Uri(Host.Admin, "media").AbsoluteUri));
        await Expect(tab.Heading("Shell.NotFound.Heading")).ToBeVisibleAsync();
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[1].Id);
        fetched.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AUserWithoutTheMediaPermissionSeesNoMediaWidgetAndItsHalfIsNeverFetched()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var fetched = HalfRequests(tab);
        await SignInOnTheGalleryAsync(browser, tab, Host.Reader);
        await Expect(tab.SiteSwitcher).ToHaveValueAsync(Host.Sites[0].Id);
        await Expect(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Media", Exact = true })).ToHaveCountAsync(0);

        await Home(tab).ClickAsync();
        await Expect(tab.Page.Locator("main").GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Article", Exact = true })).ToBeVisibleAsync();
        await Expect(tab.Page.Locator("main .home-tile")).ToHaveCountAsync(1);
        await Expect(UploadsHeading(tab)).ToHaveCountAsync(0);
        fetched.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private async Task SignInOnTheGalleryAsync(AdminBrowser browser, AdminTab tab, Editor user)
    {
        await tab.Page.GotoAsync(new Uri(Host.Admin, "system/components").AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, user);
        await Expect(tab.Sidebar).ToBeVisibleAsync();
    }

    private static ILocator Home(AdminTab tab) =>
        tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Shell.Sidebar.Home"), Exact = true });

    private static ILocator UploadsHeading(AdminTab tab) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = "Latest uploads", Exact = true });

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
