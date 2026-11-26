using System.Collections.Concurrent;
using System.Text.Json;
using System.Web;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The sign-in of A1's exit criteria, in Chromium against the reference host: through the hosted login with PKCE, back
// signed in, a reload that keeps the session, and sign-out through the hosted logout page.
public sealed class SignInTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnEditorSignsInThroughTheHostedLoginWithPkceAndAReloadKeepsTheSession()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();

        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await tab.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/account/login");

        var authorize = HttpUtility.ParseQueryString(tab.AuthorizeRequests.ShouldHaveSingleItem().Query);
        authorize["response_type"].ShouldBe("code");
        authorize["client_id"].ShouldBe("fluentcms-admin");
        authorize["redirect_uri"].ShouldBe(new Uri(Host.Admin, "authentication/login-callback").AbsoluteUri);
        authorize["code_challenge_method"].ShouldBe("S256");
        authorize["code_challenge"]!.Length.ShouldBe(43);
        authorize["scope"]!.Split(' ').ShouldBe(["openid", "profile", "email", "offline_access", "management"], ignoreOrder: true);

        var editor = await EditorAsync();
        var me = tab.NextAnswerAsync(response => response.Url == tab.ManagementUrl("me"));
        var exchange = await tab.TokenRequestOfAsync(() => browser.SignInOnTheHostedPageAsync(tab, editor));
        exchange.ShouldBeGranted("authorization_code");
        exchange.Fields.ShouldContain("code_verifier");

        // Back on the route it started from, with the code gone from the address.
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(Host.Admin.AbsoluteUri);

        // The shell's first call carries the access token as a bearer, and the Management API accepts it.
        var meAnswer = await me;
        meAnswer.Status.ShouldBe(200);
        ((await meAnswer.Request.HeaderValueAsync("authorization"))?.StartsWith("Bearer ", StringComparison.Ordinal)).ShouldBe(true);

        // The access token is in memory only, so a reload restores the session with the tab's refresh token.
        // The hosted login sends the browser back to authorize after the password, so the requests are counted from here.
        var (loginPages, authorizations) = (tab.LoginPagesShown, tab.AuthorizeRequests.Count);
        var restored = await tab.ReloadAsync();
        restored.ShouldBeGranted("refresh_token");
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.LoginPagesShown.ShouldBe(loginPages);
        tab.AuthorizeRequests.Count.ShouldBe(authorizations);

        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A sign-in that returns straight onto a route of an area completes once. The router draws the callback page again at the
    // route while it waits for the lazy administration assembly, and that page neither completes the sign-in a second time,
    // which would warn of an unknown state, nor shows that the sign-in did not complete (#3504).
    [Theory]
    [InlineData("users", "Users")]
    [InlineData("roles", "Roles")]
    public async Task ASignInStraightOntoAnAreaRouteCompletesOnceAndNeverShowsAFailure(string route, string area)
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var warnings = new ConcurrentQueue<string>();
        tab.Page.Console += (_, message) =>
        {
            if (message.Type == "warning")
            {
                warnings.Enqueue(message.Text);
            }
        };

        // Whether the failure ever shows, however briefly, in any document of the tab.
        var failed = JsonSerializer.Serialize(Repository.AdminText("Authentication.Callback.FailedHeading"));
        await tab.Page.AddInitScriptAsync(
            $"window.__callbackFailed = false; new MutationObserver(() => {{ if (!window.__callbackFailed && document.body && document.body.textContent.includes({failed})) {{ window.__callbackFailed = true; }} }}).observe(document, {{ subtree: true, childList: true, characterData: true }});");
        var address = new Uri(Host.Admin, route).AbsoluteUri;
        await tab.Page.GotoAsync(address);
        await browser.SignInOnTheHostedPageAsync(tab, await Host.CreateEditorAsync("Super Admin"));

        // Two waits, each of which takes seconds in slow mode: the shell's start after the callback, then the assembly and the page.
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        await Expect(tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.AdministrationHeading(area), Exact = true })).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(address);

        (await tab.Page.EvaluateAsync<bool>("() => window.__callbackFailed")).ShouldBeFalse("the callback page showed that the sign-in did not complete");
        warnings.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The admin revokes its refresh token, which the token endpoint refuses from then on, before the logout page is confirmed;
    // it sends no id_token_hint, so the hosted logout page asks first, and once the user confirms, the server's session is
    // over and the admin, opened again, asks for the password.
    [Fact]
    public async Task SignOutRevokesTheRefreshTokenAndTheHostedLogoutPageAsksFirst()
    {
        var editor = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        var loginPages = tab.LoginPagesShown;

        // The admin gives the revocation 3 seconds at most before it goes to the logout page.
        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email }).ClickAsync();
        var revoked = tab.NextAnswerAsync(response => response.Url == new Uri(Host.Origin, "connect/revoke").AbsoluteUri);
        await tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.UserMenu.SignOut"), Exact = true }).ClickAsync();
        var revocation = await revoked;
        revocation.Status.ShouldBe(200);

        // The token it revoked, compared without ever being printed.
        var refreshToken = HttpUtility.ParseQueryString(revocation.Request.PostData ?? "")["token"];
        string.IsNullOrEmpty(refreshToken).ShouldBeFalse();
        using (var client = Host.Client())
        using (var refused = await client.PostAsync(
            new Uri("connect/token", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken!, ["client_id"] = "fluentcms-admin" }),
            TestContext.Current.CancellationToken))
        {
            (refused.StatusCode == System.Net.HttpStatusCode.BadRequest).ShouldBeTrue("the token endpoint took the revoked refresh token");
        }

        var signOut = tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out", Exact = true });
        await Expect(signOut).ToBeVisibleAsync();
        new Uri(tab.Page.Url).AbsolutePath.ShouldBe("/account/logout");
        await signOut.ClickAsync();

        // Back at the admin's base, which finds no session in the tab and none on the server.
        await Expect(tab.LoginField).ToBeVisibleAsync();
        tab.LoginPagesShown.ShouldBeGreaterThan(loginPages);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A logout the user does not confirm on the hosted page ends the tab's session and leaves the server's: the admin, opened
    // again, signs in through the authorize redirect without the login page and comes back to its start page.
    [Fact]
    public async Task ALogoutTheUserDoesNotConfirmLeavesTheAdminSignedInWhenItIsOpenedAgain()
    {
        var editor = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        var (loginPages, authorizations) = (tab.LoginPagesShown, tab.AuthorizeRequests.Count);

        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = editor.Email }).ClickAsync();
        await tab.Page.GetByRole(AriaRole.Menuitem, new PageGetByRoleOptions { Name = Repository.AdminText("Shell.UserMenu.SignOut"), Exact = true }).ClickAsync();
        await Expect(tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out", Exact = true })).ToBeVisibleAsync();
        new Uri(tab.Page.Url).AbsolutePath.ShouldBe("/account/logout");

        var exchange = await tab.TokenRequestOfAsync(() => tab.Page.GotoAsync(Host.Admin.AbsoluteUri));
        exchange.ShouldBeGranted("authorization_code");
        await Expect(tab.Heading("Home.Page.Heading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(Host.Admin.AbsoluteUri);
        tab.LoginPagesShown.ShouldBe(loginPages);
        tab.AuthorizeRequests.Count.ShouldBe(authorizations + 1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A session the server ends, here by an operator setting a new password seconds after the sign-in, ends at the tab's next
    // refresh. Within a minute of its sign-in the admin says that the session ended rather than signing in again on its own,
    // which could loop; the user's own sign-in then asks for the password and comes back to the route the tab was on.
    [Fact]
    public async Task ASessionTheServerEndsRightAfterSignInSaysSoAndSignsInAgainToTheRoute()
    {
        var editor = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var gallery = new Uri(Host.Admin, "system/components").AbsoluteUri;
        await tab.Page.GotoAsync(gallery);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(GalleryHeading(tab)).ToBeVisibleAsync();

        // The reload comes seconds after the sign-in, well within the admin's 60-second window for "ended right after sign-in".
        editor = await Host.ResetPasswordAsync(editor);
        var refused = await tab.ReloadAsync();
        refused.GrantType.ShouldBe("refresh_token");
        refused.Status.ShouldBe(400);

        await Expect(tab.Heading("Authentication.Gate.EndedAfterSignInHeading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(gallery);
        await tab.Button("Authentication.Gate.TryAgain").ClickAsync();

        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(GalleryHeading(tab)).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(gallery);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A browser that refuses sessionStorage, as some privacy settings do, throws as a script reads window.sessionStorage. Here
    // it refuses it from the callback on, so the sign-in reaches the hosted login first: the callback says that the browser
    // blocks the tab's storage and redeems nothing, and its button goes back to the admin, whose gate says so too, offers no
    // "Try again" and starts no sign-in, nor after a reload. No page throws an unhandled exception.
    [Fact]
    public async Task ABlockedSessionStorageIsShownAtTheCallbackAndTheGateInsteadOfLooping()
    {
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var unhandled = new ConcurrentQueue<string>();
        tab.Page.Console += (_, message) =>
        {
            if (message.Text.Contains("Unhandled exception", StringComparison.OrdinalIgnoreCase))
            {
                unhandled.Enqueue(message.Text);
            }
        };
        await tab.Page.AddInitScriptAsync("""
            (() => {
              const refused = 'fluentcms-e2e/refuse-session-storage';
              try {
                if (location.pathname.endsWith('/authentication/login-callback')) { localStorage.setItem(refused, '1'); }
                if (localStorage.getItem(refused) !== '1') { return; }
              } catch { return; }
              Object.defineProperty(window, 'sessionStorage', {
                configurable: true,
                get() { throw new DOMException('The operation is insecure.', 'SecurityError'); },
              });
            })();
            """);
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, await EditorAsync());

        await Expect(tab.Heading("Authentication.Callback.FailedHeading")).ToBeVisibleAsync();
        await Expect(tab.Page.GetByText(Repository.AdminText("Authentication.Callback.StorageRefused"), new() { Exact = true })).ToBeVisibleAsync();
        new Uri(tab.Page.Url).AbsolutePath.ShouldEndWith("/authentication/login-callback");
        tab.TokenRequests.ShouldBeEmpty();
        var (loginPages, authorizations) = (tab.LoginPagesShown, tab.AuthorizeRequests.Count);

        await tab.Button("Authentication.Callback.StartAgain").ClickAsync();
        await Expect(tab.Heading("Authentication.Gate.StorageRefusedHeading")).ToBeVisibleAsync();
        await Expect(tab.Button("Authentication.Gate.TryAgain")).ToHaveCountAsync(0);
        tab.Page.Url.ShouldBe(Host.Admin.AbsoluteUri);

        await tab.Page.ReloadAsync();
        await Expect(tab.Heading("Authentication.Gate.StorageRefusedHeading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(Host.Admin.AbsoluteUri);
        tab.AuthorizeRequests.Count.ShouldBe(authorizations);
        tab.LoginPagesShown.ShouldBe(loginPages);
        tab.TokenRequests.ShouldBeEmpty();
        unhandled.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The gallery is a page of the administration assembly, with its texts in the folder Gallery there.
    private static ILocator GalleryHeading(AdminTab tab) =>
        tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Repository.AdministrationText("Gallery", "System.Components.Heading"), Exact = true });
}
