using System.Collections.Concurrent;
using System.Web;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The sign-in's callback in the browser's history (spec 2.4): it replaces itself as it completes, so neither Back nor Forward
// loads it again, and a callback the tab cannot complete, one the server refused or one loaded again, shows why with a way
// to sign in again. Each case signs in as a user of its own.
public sealed class CallbackTests(EndToEndHost host) : BrowserTest(host)
{
    private const string LoginPath = "/account/login";

    // Back goes to the hosted login page, whose entry the history keeps, and Forward to the admin, which restores the session
    // with the tab's refresh token, or from the back-forward cache of a browser that keeps the page: the code is redeemed once.
    [Fact]
    public async Task BackAndForwardAfterTheSignInNeverLoadTheCallbackAgain()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var callbacks = Callbacks(tab);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync());
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        callbacks.Count.ShouldBe(1);

        await tab.Page.GoBackAsync();
        await Expect(tab.LoginField).ToBeVisibleAsync();
        new Uri(tab.Page.Url).AbsolutePath.ShouldBe(LoginPath);
        await tab.Page.GoForwardAsync();
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(Host.Admin.AbsoluteUri);

        callbacks.Count.ShouldBe(1);
        tab.TokenRequests.Count(request => request.GrantType == "authorization_code").ShouldBe(1);
        tab.TokenRequests.ShouldAllBe(request => request.Status == 200);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The callback's address loaded again, as a reload of it or a copy of the address would, finds no sign-in of the tab to
    // complete: it redeems nothing, says so, and its button signs in again through the server's session.
    [Fact]
    public async Task ACallbackLoadedAgainRedeemsNothingAndSignsInAgainFromItsButton()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var callbacks = Callbacks(tab);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync());
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        var loginPages = tab.LoginPagesShown;

        await tab.Page.GotoAsync(callbacks.Single());
        await Expect(tab.Heading("Authentication.Callback.FailedHeading")).ToBeVisibleAsync();
        await Expect(tab.Page.GetByText(Repository.AdminText("Authentication.Callback.UnknownState"), new() { Exact = true })).ToBeVisibleAsync();
        tab.TokenRequests.Count(request => request.GrantType == "authorization_code").ShouldBe(1);

        var exchange = await tab.TokenRequestOfAsync(() => tab.Button("Authentication.Callback.SignInAgain").ClickAsync());
        exchange.ShouldBeGranted("authorization_code");
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(Host.Admin.AbsoluteUri);
        tab.LoginPagesShown.ShouldBe(loginPages);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A callback with the server's error for the tab's own sign-in, as when the user is refused, says that the server did not
    // sign the user in, and its button starts a sign-in that goes on as any other.
    [Fact]
    public async Task ACallbackTheServerRefusedSaysSoAndItsButtonStartsTheSignInAgain()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await Expect(tab.LoginField).ToBeVisibleAsync();
        var state = HttpUtility.ParseQueryString(tab.AuthorizeRequests.ShouldHaveSingleItem().Query)["state"];

        var refused = new Uri(Host.Admin, $"authentication/login-callback?error=access_denied&state={Uri.EscapeDataString(state!)}").AbsoluteUri;
        await tab.Page.GotoAsync(refused);
        await Expect(tab.Heading("Authentication.Callback.FailedHeading")).ToBeVisibleAsync();
        await Expect(tab.Page.GetByText(Repository.AdminText("Authentication.Callback.Refused"), new() { Exact = true })).ToBeVisibleAsync();
        tab.TokenRequests.ShouldBeEmpty();

        var editor = await Host.CreateEditorAsync();
        await tab.Button("Authentication.Callback.SignInAgain").ClickAsync();
        var exchange = await tab.TokenRequestOfAsync(() => browser.SignInOnTheHostedPageAsync(tab, editor));
        exchange.ShouldBeGranted("authorization_code");
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.AuthorizeRequests.Count.ShouldBeGreaterThan(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The address of each callback the tab loads.
    private static ConcurrentQueue<string> Callbacks(AdminTab tab)
    {
        var callbacks = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest && new Uri(request.Url).AbsolutePath.EndsWith("/authentication/login-callback", StringComparison.Ordinal))
            {
                callbacks.Enqueue(request.Url);
            }
        };
        return callbacks;
    }
}
