using System.Collections.Concurrent;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The steps of the hosted pages during a sign-in (spec 2.4): a forced password change and the two-factor enrolment a role
// requires. The admin sends the user to authorize, which sends the user through the step and back, and the admin completes
// the sign-in on the route it started from. Each case's user, and its role, are its own.
public sealed class SignInStepsTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Route = "system/components";

    [Fact]
    public async Task AUserWhoMustChangeItsPasswordChoosesANewOneOnTheHostedPageAndComesBackToTheRoute()
    {
        var editor = await Host.CreateEditorAsync();
        await Host.MustChangePasswordAsync(editor);
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var changes = Visits(tab, "/account/change-password");
        await tab.Page.GotoAsync(new Uri(Host.Admin, Route).AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, editor);
        await Expect(tab.Page.GetByText("You must choose a new password before you go on.", new() { Exact = true })).ToBeVisibleAsync();

        var changed = editor;
        var exchange = await tab.TokenRequestOfAsync(async () => changed = await browser.ChangePasswordOnTheHostedPageAsync(tab, editor));
        exchange.ShouldBeGranted("authorization_code");
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        await Expect(GalleryHeading(tab)).ToBeVisibleAsync();
        tab.Page.Url.ShouldBe(new Uri(Host.Admin, Route).AbsoluteUri);
        changes.Count.ShouldBe(1);

        // The new password signs in from another device, which is not asked to change it again.
        var device = await OpenBrowserAsync();
        var other = await device.NewTabAsync();
        var otherChanges = Visits(other, "/account/change-password");
        await other.Page.GotoAsync(new Uri(Host.Admin, Route).AbsoluteUri);
        await device.SignInOnTheHostedPageAsync(other, changed);
        await Expect(other.Sidebar).ToBeVisibleAsync();
        await Expect(GalleryHeading(other)).ToBeVisibleAsync();
        otherChanges.ShouldBeEmpty();
        browser.PolicyViolations.ShouldBeEmpty();
        device.PolicyViolations.ShouldBeEmpty();
    }

    // The key and the recovery codes the enrolment page shows are left out of a kept trace: the page is read and the code
    // sent in an unrecorded scope, which ends once the admin is back.
    [Fact]
    public async Task AUserWhoseRoleRequiresTwoFactorEnrolsOnTheHostedPageAndComesBackToTheRoute()
    {
        var user = await Host.CreateEditorAsync(await Host.CreateTwoFactorRoleAsync(Mark));
        var browser = await OpenBrowserAsync();
        var tab = await browser.NewTabAsync();
        var page = tab.Page;
        await page.GotoAsync(new Uri(Host.Admin, Route).AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, user);
        await Expect(page.GetByLabel("Code from the app", new() { Exact = true })).ToBeVisibleAsync();
        new Uri(page.Url).AbsolutePath.ShouldBe("/account/enable-two-factor");

        TokenRequest exchange;
        await using (await browser.UnrecordedAsync())
        {
            var key = browser.MaskAuthenticatorKey(await page.Locator("main code").First.InnerTextAsync());
            await page.GetByLabel("Code from the app", new() { Exact = true }).FillAsync(Ui.AuthenticatorCode(key));
            await page.GetByRole(AriaRole.Button, new() { Name = "Turn on two-factor sign-in", Exact = true }).ClickAsync();
            await Expect(page.GetByText("Two-factor sign-in is on.", new() { Exact = true })).ToBeVisibleAsync();

            var codes = await page.Locator("ul.codes code").AllInnerTextsAsync();
            codes.Count.ShouldBe(10);
            foreach (var code in codes)
            {
                browser.Mask(code.Trim());
            }

            exchange = await tab.TokenRequestOfAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "Continue", Exact = true }).ClickAsync());
            await Expect(tab.Sidebar).ToBeVisibleAsync();
        await Expect(GalleryHeading(tab)).ToBeVisibleAsync();
        }

        exchange.ShouldBeGranted("authorization_code");
        page.Url.ShouldBe(new Uri(Host.Admin, Route).AbsoluteUri);
        tab.LoginPagesShown.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // The gallery is a page of the administration assembly, with its texts in the folder Gallery there. The shell's start and
    // that assembly's load are waited for one after the other, since each takes seconds in slow mode.
    private static ILocator GalleryHeading(AdminTab tab) =>
        tab.Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = Repository.AdministrationText("Gallery", "System.Components.Heading"), Exact = true });

    // Each load of the hosted page at the path in the tab.
    private static ConcurrentQueue<string> Visits(AdminTab tab, string path)
    {
        var visits = new ConcurrentQueue<string>();
        tab.Page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest && request.Method == "GET" && new Uri(request.Url).AbsolutePath == path)
            {
                visits.Enqueue(path);
            }
        };
        return visits;
    }
}
