using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// An endpoint of the authorization server that never answers (spec 2.4): the admin holds each of its requests to 20 seconds
// and a sign-out to 3, then says what failed with a way to go on, rather than wait for ever. The browser holds the request
// itself, by a route that never answers, and lets it go once the case has seen the message.
public sealed class BoundedWaitTests(EndToEndHost host) : BrowserTest(host)
{
    // The admin's own bound of a request to the authorization server (AuthorityOptions.DefaultTimeout).
    private static readonly TimeSpan RequestBound = TimeSpan.FromSeconds(20);

    // A wait for the message that a bounded request leads to: the bound, then the harness's own wait for the page.
    private static readonly LocatorAssertionsToBeVisibleOptions AfterTheBound = new() { Timeout = (float)(RequestBound + AdminBrowser.Wait).TotalMilliseconds };

    [Fact]
    public async Task ADiscoveryThatHangsEndsWithTheGatesMessageAndTryAgainSignsIn()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var discovery = new Uri(Host.Origin, ".well-known/openid-configuration").AbsoluteUri;
        var held = await HoldAsync(tab, discovery);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);

        await Expect(tab.Heading("Authentication.Gate.UnavailableHeading")).ToBeVisibleAsync(AfterTheBound);
        held.Count.ShouldBe(1);
        tab.AuthorizeRequests.ShouldBeEmpty();

        await tab.Page.UnrouteAsync(discovery);
        await tab.Button("Authentication.Gate.TryAgain").ClickAsync();
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The code is the server's, so the callback that could not redeem it says the server could not be reached, and its
    // button signs in again through the server's session.
    [Fact]
    public async Task ATokenEndpointThatHangsAtTheCallbackEndsWithItsMessageAndSignInAgainCompletes()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var token = new Uri(Host.Origin, "connect/token").AbsoluteUri;
        var held = await HoldAsync(tab, token);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());

        await Expect(tab.Heading("Authentication.Callback.FailedHeading")).ToBeVisibleAsync(AfterTheBound);
        await Expect(tab.Page.GetByText(Repository.AdminText("Authentication.Callback.Unreachable"), new() { Exact = true })).ToBeVisibleAsync();
        held.Count.ShouldBe(1);

        await tab.Page.UnrouteAsync(token);
        var loginPages = tab.LoginPagesShown;
        var exchange = await tab.TokenRequestOfAsync(() => tab.Button("Authentication.Callback.SignInAgain").ClickAsync());
        exchange.ShouldBeGranted("authorization_code");
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.LoginPagesShown.ShouldBe(loginPages);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The admin clears the tab before it asks the server anything, and gives the revocation 3 seconds: a revocation that
    // hangs still ends at the hosted logout page.
    [Fact]
    public async Task ARevocationThatHangsStillSignsOutToTheHostedLogoutPage()
    {
        var editor = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "", editor);
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        var held = await HoldAsync(tab, new Uri(Host.Origin, "connect/revoke").AbsoluteUri);

        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email }).ClickAsync();
        await tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.UserMenu.SignOut"), Exact = true }).ClickAsync();
        await Expect(tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out", Exact = true })).ToBeVisibleAsync();
        new Uri(tab.Page.Url).AbsolutePath.ShouldBe("/account/logout");
        held.Count.ShouldBe(1);
        (await tab.Page.EvaluateAsync<string?>("() => sessionStorage.getItem('fluentcms-admin/session')")).ShouldBeNull();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Each request to the address is held by the browser and never answered while the route is on; the routes the page still
    // holds are let go with the page.
    private static async Task<List<IRoute>> HoldAsync(AdminTab tab, string address)
    {
        List<IRoute> held = [];
        await tab.Page.RouteAsync(address, route =>
        {
            lock (held)
            {
                held.Add(route);
            }

            return Task.CompletedTask;
        });
        return held;
    }
}
