using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A navigation that waits for a lazy assembly or for the access of a new content locale shows the shell with a loading state
// (#3446, spec 4.1): on a reload, where the router has drawn no page yet, and inside the app, where the page before stays
// below it. Each case holds the download or the read until the loading state shows, so that a fast machine does not skip it.
public sealed class NavigationProgressTests(EndToEndHost host) : BrowserTest(host)
{
    // The assemblies as the publish names them, with the fingerprint, and none of the files beside them.
    private static readonly Regex Administration = new(@"/_framework/FluentCMS\.Admin\.Administration(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    private static readonly Regex MediaHalf = new(@"/_framework/FluentCMS\.Plugins\.Media\.Admin(\.[0-9a-z]+)?\.wasm\z", RegexOptions.CultureInvariant);

    // Past the shell's limit for the reads of a new content locale, 75 s, and short of the client's 100 s per attempt.
    private static readonly LocatorAssertionsToBeVisibleOptions PastTheLimit = new() { Timeout = 95_000 };

    // Not on WebKit (spec 7): WebKit takes the assembly from its cache on the reload, and the route that holds it never sees it.
    [Fact]
    [Trait("NotOn", "webkit")]
    public async Task AReloadOfAnAdministrationRouteShowsTheShellAndALoadingStateBeforeThePage()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "users", await Host.CreateEditorAsync("Super Admin"));
        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();
        var fetched = Requests(tab, Administration);
        var download = await HoldAsync(tab, url => Administration.IsMatch(url.AbsolutePath));

        await tab.ReloadAsync();

        await Expect(Loading(tab)).ToBeVisibleAsync();
        await Expect(Entry(tab, "Administration.Menu.Users")).ToBeVisibleAsync();
        await Expect(AreaHeading(tab, "Users")).ToHaveCountAsync(0);

        download.SetResult();

        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();
        await Expect(Loading(tab)).ToHaveCountAsync(0);
        fetched.Count.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AReloadOfAHalfsRouteShowsTheShellAndALoadingStateBeforeThePage()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "media", await Host.CreateEditorAsync("Super Admin"));
        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        var fetched = Requests(tab, MediaHalf);
        var download = await HoldAsync(tab, url => MediaHalf.IsMatch(url.AbsolutePath));

        await tab.ReloadAsync();

        await Expect(Loading(tab)).ToBeVisibleAsync();
        await Expect(MediaEntry(tab)).ToBeVisibleAsync();
        await Expect(MediaHeading(tab)).ToHaveCountAsync(0);

        download.SetResult();

        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        await Expect(Loading(tab)).ToHaveCountAsync(0);
        fetched.Count.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // From the account page, which loads nothing lazily, to an area's route and then to a half's: the page before stays below
    // the loading state while the assembly downloads.
    [Fact]
    public async Task ANavigationInsideTheAppToARouteWhoseAssemblyIsNotLoadedShowsProgressAtOnce()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "account", await Host.CreateEditorAsync("Super Admin"));
        await Expect(tab.Heading("Account.Page.Heading")).ToBeVisibleAsync();
        var administration = await HoldAsync(tab, url => Administration.IsMatch(url.AbsolutePath));
        var media = await HoldAsync(tab, url => MediaHalf.IsMatch(url.AbsolutePath));

        await Entry(tab, "Administration.Menu.Users").ClickAsync();

        await Expect(Loading(tab)).ToBeVisibleAsync();
        await Expect(tab.Heading("Account.Page.Heading")).ToBeVisibleAsync();
        administration.SetResult();
        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();
        await Expect(Loading(tab)).ToHaveCountAsync(0);

        await MediaEntry(tab).ClickAsync();

        await Expect(Loading(tab)).ToBeVisibleAsync();
        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();
        media.SetResult();
        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        await Expect(Loading(tab)).ToHaveCountAsync(0);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // On an area's route and on a half's, the page stays below the loading state while the new locale's access is read.
    [Fact]
    public async Task AChangeOfTheContentLocaleShowsProgressWhileItsAccessIsRead()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "users", await Host.CreateEditorAsync("Super Admin"));
        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();

        await ChangeLocaleAsync(tab, "users?locale=fr", AreaHeading(tab, "Users"));

        await MediaEntry(tab).ClickAsync();
        await Expect(MediaHeading(tab)).ToBeVisibleAsync();
        await ChangeLocaleAsync(tab, "media?locale=en", MediaHeading(tab));
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The read of the new locale's access never answers: the shell gives up on it and shows its failed state, before the
    // client's own timeout, and Try again reads the locale's access again.
    [Fact]
    public async Task AReadOfANewLocalesAccessThatHangsEndsInAMessageWithARetry()
    {
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "users", await Host.CreateEditorAsync("Super Admin"));
        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();
        var hung = await HoldAsync(tab, url => url.AbsolutePath.EndsWith("/me/access", StringComparison.Ordinal));

        await tab.Page.EvaluateAsync("() => Blazor.navigateTo('users?locale=fr')");

        await Expect(Loading(tab)).ToBeVisibleAsync();
        await Expect(tab.Heading("Shell.Start.FailedHeading")).ToBeVisibleAsync(PastTheLimit);
        await tab.Page.UnrouteAllAsync(new PageUnrouteAllOptions { Behavior = UnrouteBehavior.IgnoreErrors });
        hung.SetResult();

        await tab.Button("Shell.Start.TryAgain").ClickAsync();

        await Expect(AreaHeading(tab, "Users")).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Uri(Host.Admin, "users?locale=fr").AbsoluteUri);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static async Task ChangeLocaleAsync(AdminTab tab, string route, ILocator heading)
    {
        var read = await HoldAsync(tab, url => url.AbsolutePath.EndsWith("/me/access", StringComparison.Ordinal));

        await tab.Page.EvaluateAsync($"() => Blazor.navigateTo('{route}')");

        await Expect(Loading(tab)).ToBeVisibleAsync();
        await Expect(heading).ToBeVisibleAsync();
        read.SetResult();
        await tab.Page.UnrouteAllAsync(new PageUnrouteAllOptions { Behavior = UnrouteBehavior.Wait });
        await Expect(Loading(tab)).ToHaveCountAsync(0);
        await Expect(heading).ToBeVisibleAsync();
        await Expect(tab.Page).ToHaveURLAsync(new Regex(Regex.Escape(route) + "$"));
    }

    // The requests the predicate matches wait until the returned source is set; one the page has given up on meanwhile is
    // left as it is.
    private static async Task<TaskCompletionSource> HoldAsync(AdminTab tab, Func<Uri, bool> held)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await tab.Page.RouteAsync(url => held(new Uri(url)), async route =>
        {
            await release.Task;
            try
            {
                await route.ContinueAsync();
            }
            catch (PlaywrightException)
            {
            }
        });
        return release;
    }

    private static ILocator Loading(AdminTab tab) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Status).GetByText(Repository.AdminText("Shell.Layout.LoadingPage"), new LocatorGetByTextOptions { Exact = true });

    private static ILocator Entry(AdminTab tab, string resourceKey) =>
        tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText(resourceKey), Exact = true });

    private static ILocator MediaEntry(AdminTab tab) =>
        tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Media", Exact = true });

    private static ILocator AreaHeading(AdminTab tab, string area) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.AdministrationHeading(area), Exact = true });

    private static ILocator MediaHeading(AdminTab tab) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.MediaText("Media.Page.Heading"), Exact = true });

    private static ConcurrentQueue<string> Requests(AdminTab tab, Regex file)
    {
        var fetched = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (file.IsMatch(new Uri(request.Url).AbsolutePath))
            {
                fetched.Enqueue(request.Url);
            }
        };
        return fetched;
    }
}
