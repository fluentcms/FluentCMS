using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Core.IntegrationTests.Members.MemberProviderFixture;
using AuthorizeRequest = FluentCMS.Core.IntegrationTests.Members.MemberPagesTests.AuthorizeRequest;

namespace FluentCMS.Core.IntegrationTests.Members;

// A member's external logins: linking on the hosted pages, and listing and removing them on the pages and the APIs.
public sealed partial class MemberProviderTests
{
    private const string LoginsPage = "/account/members/logins";

    [Fact]
    public async Task AMemberLinksASecondProviderListsItsLoginsAndRemovesOneAndTheLastWayInIsNotRemoved()
    {
        var address = MemberFixture.NewAddress();
        var google = FakeAccount.Of(address);
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.6.1");
        using (var back = await SignInWithAsync(browser, Google, google))
        {
            (await DecideAsync(browser, back)).ShouldBe("/");
        }

        var member = (await MemberAsync(fixture.Host, address)).ShouldNotBeNull();
        var signedIn = new TestMember(member.Id, address, new Principal(PrincipalKind.Member, member.Id, address));
        var id = member.Id.ToString("D");

        // The member has no password, so its one login is its last way in: neither the API nor the page removes it.
        using (var api = await fixture.Host.AsMemberAsync(signedIn))
        {
            var last = await MemberFixture.SendAsync(api, HttpMethod.Delete, MemberFixture.Api + "me/logins/google");
            last.Status.ShouldBe(HttpStatusCode.Conflict);
            last.Json["code"]!.GetValue<string>().ShouldBe(MemberErrorCodes.LastWayIn);

            var none = await MemberFixture.SendAsync(api, HttpMethod.Delete, MemberFixture.Api + "me/logins/github");
            none.Status.ShouldBe(HttpStatusCode.NotFound);
            none.Json["code"]!.GetValue<string>().ShouldBe(MemberErrorCodes.LoginNotFound);
        }

        var page = await browser.PageAsync(LoginsPage);
        page.ShouldContain(address);
        page.ShouldContain("Remove Google");
        page.ShouldContain("Link GitHub");
        page.ShouldNotContain("Link Google");
        using (var kept = await browser.PostAsync(LoginsPage + "?handler=Remove", page, new Dictionary<string, string> { ["provider"] = Google }))
        {
            kept.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await kept.Content.ReadAsStringAsync(Token)).ShouldContain("last way to sign in");
        }

        (await LoginsAsync(fixture.Host, member.Id)).ShouldHaveSingleItem();

        // Linking goes to the provider and comes back to the page, which links nothing until the member says so.
        var githubAddress = MemberFixture.NewAddress();
        var github = FakeAccount.Of(null) with { Login = "octo", Name = "Octo Member", Addresses = [(githubAddress, true, true)] };
        using (var challenge = await browser.PostAsync(LoginsPage + "?handler=Link", page, new Dictionary<string, string> { ["provider"] = GitHub }))
        {
            challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            using var back = await AnswerAsync(browser, GitHub, challenge.Headers.Location!, github);
            back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            back.Headers.Location!.OriginalString.ShouldBe(LoginsPage);
        }

        (await LoginsAsync(fixture.Host, member.Id)).ShouldHaveSingleItem();
        var asked = await browser.PageAsync(LoginsPage);
        asked.ShouldContain("Link the GitHub account");
        asked.ShouldContain(githubAddress);
        using (var confirmed = await browser.PostAsync(LoginsPage + "?handler=Confirm", asked, new Dictionary<string, string>()))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            confirmed.Headers.Location!.OriginalString.ShouldBe(LoginsPage + "?Status=linked");
        }

        browser.HasCookie(ExternalCookie).ShouldBeFalse();
        (await LoginsAsync(fixture.Host, member.Id)).Select(login => login.LoginProvider).ShouldBe([GitHub, Google]);

        // The address of the linked account is not the member's and makes no member.
        (await MemberAsync(fixture.Host, githubAddress)).ShouldBeNull();

        // The member reads its logins: the provider and its display name, and not the key the provider knows it by.
        var tokens = await fixture.Host.SignInAsync(signedIn);
        using (var api = fixture.Host.CreateSecureClient())
        {
            api.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
            var list = await MemberFixture.SendAsync(api, HttpMethod.Get, MemberFixture.Api + "me/logins");
            list.Status.ShouldBe(HttpStatusCode.OK);
            list.Json.ToJsonString().ShouldBe("""{"items":[{"provider":"github","displayName":"GitHub"},{"provider":"google","displayName":"Google"}]}""");
            list.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }

        // The linked account signs the member in.
        using (var other = new HostedPagesBrowser(fixture.Host, "10.7.6.2"))
        {
            var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid");
            using var back = await SignInWithAsync(other, GitHub, github, request.Url);
            var viaGitHub = await request.ExchangeAsync(fixture.Host, await other.FollowAsync(back));
            MemberTokenTests.Payload(viaGitHub.GetProperty("access_token").GetString()!).GetProperty("sub").GetString().ShouldBe(id);
        }

        // An administrator reads them with members.manage, and no one without it.
        using var admin = await fixture.Host.AsUserAsync(await fixture.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        using (var editor = await fixture.Host.AsUserAsync(await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)))
        {
            var read = await MemberFixture.SendAsync(admin, HttpMethod.Get, $"{MemberFixture.Management}members/{id}/logins");
            read.Json.ToJsonString().ShouldBe("""{"items":[{"provider":"github","displayName":"GitHub"},{"provider":"google","displayName":"Google"}]}""");
            (await MemberFixture.SendAsync(editor, HttpMethod.Get, $"{MemberFixture.Management}members/{id}/logins")).Status.ShouldBe(HttpStatusCode.Forbidden);
            (await MemberFixture.SendAsync(editor, HttpMethod.Delete, $"{MemberFixture.Management}members/{id}/logins/google")).Status.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Removing a login gives a new security stamp: the member's other sessions end, tokens included, and the
        // browser that removed it stays signed in.
        var stamp = member.SecurityStamp;
        page = await browser.PageAsync(LoginsPage);
        using (var removed = await browser.PostAsync(LoginsPage + "?handler=Remove", page, new Dictionary<string, string> { ["provider"] = Google }))
        {
            removed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            removed.Headers.Location!.OriginalString.ShouldBe(LoginsPage + "?Status=removed");
        }

        (await LoginsAsync(fixture.Host, member.Id)).ShouldHaveSingleItem().LoginProvider.ShouldBe(GitHub);
        (await MemberAsync(fixture.Host, address))!.SecurityStamp.ShouldNotBe(stamp);
        (await browser.PageAsync(LoginsPage)).ShouldContain("Link Google");
        using (var refresh = await browser.PostAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = FluentCmsTestHost.OAuthMemberClientId,
            ["refresh_token"] = tokens.RefreshToken!,
        }))
        {
            refresh.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var api = fixture.Host.CreateSecureClient())
        {
            api.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
            (await MemberFixture.SendAsync(api, HttpMethod.Get, MemberFixture.Api + "me")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The account that was removed is a stranger's again, and the member's address is not taken over by it.
        using (var other = new HostedPagesBrowser(fixture.Host, "10.7.6.3"))
        {
            using var back = await SignInWithAsync(other, Google, google);
            using var refused = await other.FollowAsync(back);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(Refused);
            other.HasCookie(MemberCookie).ShouldBeFalse();
        }

        // The administrator does not remove the last way in either; with a password the login is no longer the last.
        var lastOne = await MemberFixture.SendAsync(admin, HttpMethod.Delete, $"{MemberFixture.Management}members/{id}/logins/github");
        lastOne.Status.ShouldBe(HttpStatusCode.Conflict);
        lastOne.Json["code"]!.GetValue<string>().ShouldBe(MemberErrorCodes.LastWayIn);
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<MemberManager>();
            (await members.AddPasswordAsync((await members.FindByIdAsync(id))!, MemberFixture.Password)).Succeeded.ShouldBeTrue();
        }

        (await MemberFixture.SendAsync(admin, HttpMethod.Delete, $"{MemberFixture.Management}members/{id}/logins/github")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await LoginsAsync(fixture.Host, member.Id)).ShouldBeEmpty();
        (await MemberFixture.SendAsync(admin, HttpMethod.Delete, $"{MemberFixture.Management}members/{id}/logins/github")).Status.ShouldBe(HttpStatusCode.NotFound);

        // Each link and removal is an event with an activity row, in the name of whoever made it.
        var rows = await MemberFixture.ActivityAsync(fixture.Host, "member.login_", id);
        rows.Where(row => row.Action == "member.login_linked").Select(row => (Details(row)["provider"], row.ActorType))
            .ShouldBe([(Google, PrincipalKind.Member), (GitHub, PrincipalKind.Member)], ignoreOrder: true);
        rows.Where(row => row.Action == "member.login_unlinked").Select(row => (Details(row)["provider"], row.ActorType))
            .ShouldBe([(Google, PrincipalKind.Member), (GitHub, PrincipalKind.AdminUser)], ignoreOrder: true);
    }

    [Fact]
    public async Task ALoginIsLinkedOnlyByTheMemberThatAskedAndOnlyOnceToOneMember()
    {
        var first = await fixture.Host.CreateMemberAsync(password: MemberFixture.Password, cancellationToken: Token);
        var second = await fixture.Host.CreateMemberAsync(password: MemberFixture.Password, cancellationToken: Token);
        var account = FakeAccount.Of(MemberFixture.NewAddress());
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.7.1");

        // Nobody is signed in: the page sends the visitor to the login page, and links nothing.
        using (var anonymous = await browser.GetAsync(LoginsPage))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            anonymous.Headers.Location!.OriginalString.ShouldBe("/account/members/login?ReturnUrl=%2Faccount%2Fmembers%2Flogins");
        }

        // The first member asks to link and comes back from the provider.
        await SignInWithPasswordAsync(browser, first.Email);
        await StartLinkAsync(browser, Google, account);

        // The page that signs in does not take a sign-in that was started to link, and it is spent by the attempt.
        using (var misused = await browser.GetAsync("/account/members/external-login"))
        {
            misused.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            misused.Headers.Location!.OriginalString.ShouldBe("/account/members/login?ReturnUrl=%2F&Status=external-failed");
        }

        browser.HasCookie(ExternalCookie).ShouldBeFalse();
        (await browser.PageAsync(LoginsPage)).ShouldNotContain("Link the Google account");

        // Another member in the same browser neither sees nor confirms what the first one started.
        await StartLinkAsync(browser, Google, account);
        using (var signedOut = await browser.SubmitAsync("/account/members/logout", new Dictionary<string, string>()))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        await SignInWithPasswordAsync(browser, second.Email);
        var page = await browser.PageAsync(LoginsPage);
        page.ShouldContain(second.Email);
        page.ShouldNotContain("Link the Google account");
        using (var confirmed = await browser.PostAsync(LoginsPage + "?handler=Confirm", page, new Dictionary<string, string>()))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            confirmed.Headers.Location!.OriginalString.ShouldBe(LoginsPage + "?Status=external-failed");
        }

        (await LoginsAsync(fixture.Host, first.Id)).ShouldBeEmpty();
        (await LoginsAsync(fixture.Host, second.Id)).ShouldBeEmpty();

        // The second member links the account itself; the first one is then told that it is another member's.
        await StartLinkAsync(browser, Google, account);
        page = await browser.PageAsync(LoginsPage);
        using (var confirmed = await browser.PostAsync(LoginsPage + "?handler=Confirm", page, new Dictionary<string, string>()))
        {
            confirmed.Headers.Location!.OriginalString.ShouldBe(LoginsPage + "?Status=linked");
        }

        using var other = new HostedPagesBrowser(fixture.Host, "10.7.7.2");
        await SignInWithPasswordAsync(other, first.Email);
        await StartLinkAsync(other, Google, account);
        page = await other.PageAsync(LoginsPage);
        using (var taken = await other.PostAsync(LoginsPage + "?handler=Confirm", page, new Dictionary<string, string>()))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await taken.Content.ReadAsStringAsync(Token)).ShouldContain("is linked to another account here");
        }

        (await LoginsAsync(fixture.Host, first.Id)).ShouldBeEmpty();
        (await LoginsAsync(fixture.Host, second.Id)).ShouldHaveSingleItem().ProviderKey.ShouldBe(account.Subject);

        // Cancelling ends the sign-in at the provider without linking it.
        var spare = FakeAccount.Of(MemberFixture.NewAddress());
        await StartLinkAsync(other, Corp, spare);
        page = await other.PageAsync(LoginsPage);
        page.ShouldContain("Link the Corp account");
        using (var cancelled = await other.PostAsync(LoginsPage + "?handler=Cancel", page, new Dictionary<string, string>()))
        {
            cancelled.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        other.HasCookie(ExternalCookie).ShouldBeFalse();
        (await LoginsAsync(fixture.Host, first.Id)).ShouldBeEmpty();

        // The second member, who has a password, removes its login with its token, which ends with its sessions.
        using var api = await fixture.Host.AsMemberAsync(second);
        (await MemberFixture.SendAsync(api, HttpMethod.Delete, MemberFixture.Api + "me/logins/GOOGLE")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await LoginsAsync(fixture.Host, second.Id)).ShouldBeEmpty();
        (await MemberFixture.SendAsync(api, HttpMethod.Get, MemberFixture.Api + "me/logins")).Status.ShouldBe(HttpStatusCode.Unauthorized);

        // Neither an admin user's token nor no token reads a member's logins there.
        using var admin = await fixture.Host.AsUserAsync(await fixture.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        (await MemberFixture.SendAsync(admin, HttpMethod.Get, MemberFixture.Api + "me/logins")).Status.ShouldBe(HttpStatusCode.Forbidden);
        using var nobody = fixture.Host.CreateSecureClient();
        (await MemberFixture.SendAsync(nobody, HttpMethod.Delete, MemberFixture.Api + "me/logins/google")).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LinkingRemovingAndStartingASignInNeedTheAntiforgeryTokenOfThePage()
    {
        var member = await fixture.Host.CreateMemberAsync(password: MemberFixture.Password, cancellationToken: Token);
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.8.1");
        await SignInWithPasswordAsync(browser, member.Email);
        await StartLinkAsync(browser, Google, FakeAccount.Of(MemberFixture.NewAddress()));

        // A form another site posts carries the member's cookies and no token.
        foreach (var url in new[]
        {
            LoginsPage + "?handler=Confirm",
            LoginsPage + "?handler=Link",
            LoginsPage + "?handler=Remove",
            LoginsPage + "?handler=Cancel",
            "/account/members/login?handler=External",
            "/account/members/register?handler=External",
        })
        {
            using var forged = await browser.PostAsync(url, new Dictionary<string, string> { ["provider"] = Google });
            forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest, url);
        }

        (await LoginsAsync(fixture.Host, member.Id)).ShouldBeEmpty();
        browser.HasCookie(ExternalCookie).ShouldBeTrue();

        // A provider the pool does not have is not challenged, whatever the form names.
        var page = await browser.PageAsync(LoginsPage);
        foreach (var name in new[] { "member-google", "Identity.External", "FluentCms.Members", "facebook", "" })
        {
            using var unknown = await browser.PostAsync(LoginsPage + "?handler=Link", page, new Dictionary<string, string> { ["provider"] = name });
            unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
        }
    }

    private static async Task SignInWithPasswordAsync(HostedPagesBrowser browser, string address)
    {
        using var response = await browser.SubmitAsync("/account/members/login", MemberTokenTests.Credentials(address, MemberFixture.Password));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    // The signed-in member presses the link button, signs in at the provider and is sent back to the page.
    private async Task StartLinkAsync(HostedPagesBrowser browser, string provider, FakeAccount account)
    {
        var page = await browser.PageAsync(LoginsPage);
        using var challenge = await browser.PostAsync(LoginsPage + "?handler=Link", page, new Dictionary<string, string> { ["provider"] = provider });
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync(Token));
        using var back = await AnswerAsync(browser, provider, challenge.Headers.Location!, account);
        back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        back.Headers.Location!.OriginalString.ShouldBe(LoginsPage);
    }
}
