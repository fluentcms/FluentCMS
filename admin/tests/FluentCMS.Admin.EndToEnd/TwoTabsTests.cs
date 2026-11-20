using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// Two tabs of one browser share the server's sign-in cookie but not sessionStorage, so each holds a session of its own
// (spec 2.4, AQ3). The host's access tokens last five seconds, so a tab refreshes on demand at a call after half of that.
public sealed class TwoTabsTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task TwoTabsEachRefreshTheirOwnTokenAtOnceAndBothStaySignedIn()
    {
        var browser = await OpenBrowserAsync();
        var first = await SignedInTabAsync(browser, "");
        await Expect(first.Sidebar).ToBeVisibleAsync();

        // The second tab signs in through the authorize redirect, which the server's session answers with no login page.
        var second = await browser.NewTabAsync();
        await second.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await Expect(second.Sidebar).ToBeVisibleAsync();
        second.AuthorizeRequests.Count.ShouldBe(1);
        second.LoginPagesShown.ShouldBe(0);
        second.TokenRequests.First().ShouldBeGranted("authorization_code");

        // Neither goes back to the authorization server from here: the login of the first sent it to authorize twice.
        var (firstAuthorizations, secondAuthorizations) = (first.AuthorizeRequests.Count, second.AuthorizeRequests.Count);

        // Both switch site at once until each has refreshed on demand, each with its own refresh token.
        await RefreshAtOnceAsync(first, second);

        // And a reload of both at once restores each session from its own rotated refresh token; one rotated just as a reload
        // starts is taken within the server's 30-second reuse leeway.
        var reloads = await Task.WhenAll(first.ReloadAsync(), second.ReloadAsync());
        reloads[0].ShouldBeGranted("refresh_token");
        reloads[1].ShouldBeGranted("refresh_token");
        await Expect(first.Sidebar).ToBeVisibleAsync();
        await Expect(second.Sidebar).ToBeVisibleAsync();
        first.AuthorizeRequests.Count.ShouldBe(firstAuthorizations);
        second.AuthorizeRequests.Count.ShouldBe(secondAuthorizations);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // A duplicated tab starts with a copy of the first one's sessionStorage, its refresh token included, as a tab the admin
    // opens with window.open does. The first still answers for that session, so the copy forgets it and signs in through the
    // authorize redirect: each holds a refresh token of its own, and neither depends on the server's reuse leeway.
    [Fact]
    public async Task ADuplicatedTabSignsInForItselfAndBothStaySignedIn()
    {
        var browser = await OpenBrowserAsync();
        var first = await SignedInTabAsync(browser, "");
        await Expect(first.Sidebar).ToBeVisibleAsync();
        var (loginPages, firstAuthorizations) = (first.LoginPagesShown, first.AuthorizeRequests.Count);

        var copy = await browser.OpenedByAsync(first);
        await Expect(copy.Sidebar).ToBeVisibleAsync();
        copy.AuthorizeRequests.Count.ShouldBe(1);
        copy.LoginPagesShown.ShouldBe(0);
        copy.TokenRequests.First().ShouldBeGranted("authorization_code");
        copy.TokenRequests.Count(request => request.GrantType == "authorization_code").ShouldBe(1);
        (await StoredSessionAsync(copy) == await StoredSessionAsync(first)).ShouldBeFalse("the copy holds the first tab's session");

        await RefreshAtOnceAsync(first, copy);
        var reloads = await Task.WhenAll(first.ReloadAsync(), copy.ReloadAsync());
        reloads[0].ShouldBeGranted("refresh_token");
        reloads[1].ShouldBeGranted("refresh_token");
        await Expect(first.Sidebar).ToBeVisibleAsync();
        await Expect(copy.Sidebar).ToBeVisibleAsync();
        first.LoginPagesShown.ShouldBe(loginPages);
        first.AuthorizeRequests.Count.ShouldBe(firstAuthorizations);
        copy.AuthorizeRequests.Count.ShouldBe(1);
        browser.PolicyViolations.ShouldBeEmpty();
    }

    // Compared, never written out, since it holds the refresh token.
    private static Task<string?> StoredSessionAsync(AdminTab tab) =>
        tab.Page.EvaluateAsync<string?>("() => sessionStorage.getItem('fluentcms-admin/session')");

    // A switch reads the schema, the plugins and the access with the tab's access token, which the tab refreshes first once
    // half its lifetime is over. The test waits until both tabs' tokens are due, then both switch at once, so that both
    // refresh at once: a loop of switches until a refresh would spend the user's request budget as fast as the runner allows.
    private async Task RefreshAtOnceAsync(AdminTab one, AdminTab other)
    {
        var before = (One: one.TokenRequests.Count, Other: other.TokenRequests.Count);
        await Task.WhenAll(one.TokenDueAsync(), other.TokenDueAsync());
        var sites = await Task.WhenAll(OtherSiteAsync(one), OtherSiteAsync(other));
        var accesses = await Task.WhenAll(one.SwitchSiteAsync(sites[0]), other.SwitchSiteAsync(sites[1]));
        accesses.ShouldAllBe(access => access.Status == 200);
        foreach (var (tab, count) in new[] { (one, before.One), (other, before.Other) })
        {
            await Ui.UntilAsync(() => tab.TokenRequests.Skip(count).Any(request => request.GrantType == "refresh_token"), "a tab did not refresh its token");
            tab.TokenRequests.Skip(count).ShouldAllBe(request => request.GrantType == "refresh_token" && request.Status == 200);
        }
    }

    private async Task<Site> OtherSiteAsync(AdminTab tab)
    {
        var current = await tab.SiteSwitcher.InputValueAsync();
        return Host.Sites.First(site => !string.Equals(site.Id, current, StringComparison.OrdinalIgnoreCase));
    }
}
