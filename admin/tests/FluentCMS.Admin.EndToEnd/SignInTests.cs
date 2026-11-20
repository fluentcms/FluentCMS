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

        await tab.Page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/account/logout");
        await tab.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign out", Exact = true }).ClickAsync();

        // Back at the admin's base, which finds no session in the tab and none on the server.
        await Expect(tab.LoginField).ToBeVisibleAsync();
        tab.LoginPagesShown.ShouldBeGreaterThan(loginPages);
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
        await Expect(tab.Heading("System.Components.Heading")).ToBeVisibleAsync();

        // The reload comes seconds after the sign-in, well within the admin's 60-second window for "ended right after sign-in".
        editor = await Host.ResetPasswordAsync(editor);
        var refused = await tab.ReloadAsync();
        refused.GrantType.ShouldBe("refresh_token");
        refused.Status.ShouldBe(400);

        await Expect(tab.Heading("Authentication.Gate.EndedAfterSignInHeading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(gallery);
        await tab.Button("Authentication.Gate.TryAgain").ClickAsync();

        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(tab.Heading("System.Components.Heading")).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(gallery);
        browser.PolicyViolations.ShouldBeEmpty();
    }
}
