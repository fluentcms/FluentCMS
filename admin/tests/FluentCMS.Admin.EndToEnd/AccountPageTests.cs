using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The account page of the signed-in user (spec 4.3, #3147) against the reference host: its sessions, two-factor sign-in by
// choice and the password change. Each case changes its user, so each signs in as one of its own. The preferences card is
// PreferencesTests'. The reference host has no external provider, so no case removes a login.
public sealed class AccountPageTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Route = "account";

    // The rows are the newest first, so this tab's sign-in, the later one, is the first: the other device's is ended, and
    // that device's next refresh is refused, while this tab goes on.
    [Fact]
    public async Task TheSessionsAreListedAndEndingOneStopsThatSignInAtItsNextRefresh()
    {
        var user = await Host.CreateEditorAsync();
        var device = await OpenBrowserAsync();
        var other = await SignedInTabAsync(device, "", user);
        await Expect(other.Sidebar).ToBeVisibleAsync();

        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, Route, user);
        var card = Card(tab, "Account.Sessions.Heading");
        var rows = card.Locator("li.account-item");
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(rows.Last).ToContainTextAsync("FluentCMS admin");

        var ended = await tab.AnswerAsync(() => rows.Last.Locator("fluent-button").ClickAsync(), response => response.Request.Method == "DELETE" && response.Url.StartsWith(tab.ManagementUrl("me/sessions/"), StringComparison.Ordinal));
        ended.Status.ShouldBe(204);
        await Expect(card.Locator(".account-message")).ToHaveTextAsync(Ui.Format(Text("Account.Sessions.Ended"), "FluentCMS admin"));
        await Expect(rows).ToHaveCountAsync(1);

        var refused = await other.ReloadAsync();
        refused.GrantType.ShouldBe("refresh_token");
        refused.Status.ShouldBe(400);
        browser.PolicyViolations.ShouldBeEmpty();
        device.PolicyViolations.ShouldBeEmpty();
    }

    // The key and the recovery codes are shown once, and with the codes typed back on the hosted page and into the card, they
    // are read, typed and gone in unrecorded scopes, so that a kept trace holds none of them. Turning two-factor on and off
    // each ends the user's sessions, the browser's own sign-in with them, so the page signs in again with the password: with
    // a recovery code after turning it on, with the password alone after turning it off.
    [Fact]
    public async Task TwoFactorIsTurnedOnWithACodeOfTheShownKeyShowsItsRecoveryCodesOnceAndIsTurnedOffWithOne()
    {
        var user = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, Route, user);
        var page = tab.Page;
        var card = Card(tab, "Account.TwoFactor.Heading");
        var codesShown = page.Locator("pre.account-codes");
        await Expect(card.GetByText(Text("Account.TwoFactor.Off"), new() { Exact = true })).ToBeVisibleAsync();

        var codes = await browser.UnrecordedAsync(async () =>
        {
            await Ui.ButtonIn(card, Text("Account.TwoFactor.Enrol")).ClickAsync();
            var key = browser.MaskAuthenticatorKey(await card.Locator("code.account-key").InnerTextAsync());
            await card.GetByRole(AriaRole.Textbox).FillAsync(Ui.AuthenticatorCode(key));
            await Ui.ButtonIn(card, Text("Account.TwoFactor.Verify")).ClickAsync();

            var title = Text("Account.TwoFactor.CodesHeading");
            await Expect(tab.ShownDialog(title)).ToBeVisibleAsync();
            var shown = (await tab.Dialog(title).Locator("pre.account-codes").InnerTextAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            shown.Length.ShouldBe(10);
            foreach (var code in shown)
            {
                browser.Mask(code);
            }

            await Ui.ButtonIn(tab.Dialog(title), Text("Account.TwoFactor.CodesSaved")).ClickAsync();
            await browser.SignInOnTheHostedPageAsync(tab, user);
            var recovery = page.GetByRole(AriaRole.Link, new() { Name = "Use a recovery code", Exact = true });
            await Expect(recovery).ToBeVisibleAsync();
            new Uri(page.Url).AbsolutePath.ShouldBe("/account/two-factor");
            await recovery.ClickAsync();
            await page.GetByLabel("Recovery code", new() { Exact = true }).FillAsync(shown[0]);
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
            await Expect(card.GetByText(Ui.Format(Text("Account.TwoFactor.On"), "9"), new() { Exact = true })).ToBeVisibleAsync();
            return shown;
        });

        page.Url.ShouldBe(new Uri(Host.Admin, Route).AbsoluteUri);
        await Expect(codesShown).ToHaveCountAsync(0);
        await tab.ReloadAsync();
        await Expect(card.GetByText(Ui.Format(Text("Account.TwoFactor.On"), "9"), new() { Exact = true })).ToBeVisibleAsync();
        await Expect(codesShown).ToHaveCountAsync(0);

        await Ui.ButtonIn(card, Text("Account.TwoFactor.Disable")).ClickAsync();
        await browser.UnrecordedAsync(async () =>
        {
            await card.GetByRole(AriaRole.Textbox).FillAsync(codes[1]);
            await Ui.ButtonIn(card, Text("Account.TwoFactor.Disable")).ClickAsync();
            await Expect(tab.LoginField).ToBeVisibleAsync();
        });

        await browser.SignInOnTheHostedPageAsync(tab, user);
        await Expect(card.GetByText(Text("Account.TwoFactor.Off"), new() { Exact = true })).ToBeVisibleAsync();
        page.Url.ShouldBe(new Uri(Host.Admin, Route).AbsoluteUri);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The host answers FCMS3065 only to a sign-in more than ten minutes old, a constant of its own, so the browser answers the
    // first setup so, as the host does. "Sign in again" then asks for the login page with prompt=login, although the browser's
    // own sign-in still holds, and the page comes back to the setup, which the host takes from the new token, the turn-on too.
    [Fact]
    public async Task ASetupWithASignInTooOldSignsInAgainOnTheLoginPageAndGoesOn()
    {
        var user = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, Route, user);
        var page = tab.Page;
        var card = Card(tab, "Account.TwoFactor.Heading");
        await Expect(card.GetByText(Text("Account.TwoFactor.Off"), new() { Exact = true })).ToBeVisibleAsync();
        await page.RouteAsync(
            tab.ManagementUrl("me/two-factor/enrol"),
            route => route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 403,
                ContentType = "application/problem+json",
                Body = """{"status":403,"code":"FCMS3065","title":"Forbidden"}""",
            }),
            new PageRouteOptions { Times = 1 });

        await Ui.ButtonIn(card, Text("Account.TwoFactor.Enrol")).ClickAsync();
        await Expect(card.Locator(".account-message")).ToHaveTextAsync(Text("Account.TwoFactor.SignInAgainFirst"));
        await Ui.ButtonIn(card, Text("Account.TwoFactor.SignInAgain")).ClickAsync();

        // The setup starts as the page comes back, so its key is shown in an unrecorded scope from the sign-in on.
        await browser.UnrecordedAsync(async () =>
        {
            await browser.SignInOnTheHostedPageAsync(tab, user);
            var key = browser.MaskAuthenticatorKey(await card.Locator("code.account-key").InnerTextAsync());
            page.Url.ShouldBe(new Uri(Host.Admin, Route).AbsoluteUri);
            await card.GetByRole(AriaRole.Textbox).FillAsync(Ui.AuthenticatorCode(key));
            await Ui.ButtonIn(card, Text("Account.TwoFactor.Verify")).ClickAsync();
            var title = Text("Account.TwoFactor.CodesHeading");
            await Expect(tab.ShownDialog(title)).ToBeVisibleAsync();
            foreach (var code in (await tab.Dialog(title).Locator("pre.account-codes").InnerTextAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                browser.Mask(code);
            }
        });

        tab.LoginPagesShown.ShouldBe(2);
        // The host's redirect back to the authorize endpoint after its login page keeps the request, prompt=login with it, so
        // the requests that ask for it are those of one sign-in, its state the same.
        var states = tab.AuthorizeRequests.Select(request => System.Web.HttpUtility.ParseQueryString(request.Query)).Where(query => query["prompt"] == "login").Select(query => query["state"]).ToList();
        states.ShouldNotBeEmpty();
        states.Distinct().Count().ShouldBe(1);
        tab.AuthorizeRequests.First().Query.ShouldNotContain("prompt=");
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The change ends the user's sessions within a minute of this tab's sign-in, so back here the tab's refresh is refused;
    // the tab signs in again at once, which the browser's own sign-in, kept by the hosted page, answers without the login page.
    [Fact]
    public async Task ThePasswordIsChangedOnTheHostedPageAndTheTabComesBackHereSignedIn()
    {
        var user = await Host.CreateEditorAsync();
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, Route, user);

        await Card(tab, "Account.Password.Heading").GetByRole(AriaRole.Link, new() { Name = Text("Account.Password.Change"), Exact = true }).ClickAsync();
        await browser.ChangePasswordOnTheHostedPageAsync(tab, user);

        await Expect(Card(tab, "Account.Sessions.Heading")).ToBeVisibleAsync();
        await Ui.UntilAsync(() => tab.TokenRequests.Count(request => request is { GrantType: "authorization_code", Status: 200 }) == 2, "the tab signs in again with a code of its own");
        tab.TokenRequests.ShouldContain(request => request.GrantType == "refresh_token" && request.Status == 400);
        tab.Page.Url.ShouldBe(new Uri(Host.Admin, Route).AbsoluteUri);
        tab.LoginPagesShown.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key) => Repository.AdministrationText("Account", key);

    // A card of the page, the region its heading names.
    private static ILocator Card(AdminTab tab, string headingKey) =>
        tab.Page.GetByRole(AriaRole.Region, new PageGetByRoleOptions { Name = Text(headingKey), Exact = true });
}
