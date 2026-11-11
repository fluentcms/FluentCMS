using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using static FluentCMS.Core.IntegrationTests.Members.MemberProviderFixture;
using AuthorizeRequest = FluentCMS.Core.IntegrationTests.Members.MemberPagesTests.AuthorizeRequest;

namespace FluentCMS.Core.IntegrationTests.Members;

// What a provider sign-in must not do: reach the other pool, complete without a sign-in started here, take a member
// over, send the browser elsewhere, or tell a secret.
public sealed partial class MemberProviderTests
{
    [Fact]
    public async Task AnAdminSsoProviderOffersNothingOnTheMemberPagesAndAMemberProviderNothingOnTheAdminLoginPage()
    {
        // A provider of each pool with the same name, and one that only the admin pool has.
        const string Staff = "staff";
        await using var host = NewHost(fixture.Providers, fixture.Oidc, fixture.Emails, configure: cms =>
        {
            foreach (var name in new[] { Google, Staff })
            {
                cms.Sso.Providers[name] = new SsoProvider
                {
                    Authority = fixture.Oidc.Issuer,
                    ClientId = FakeOidcProvider.ClientId,
                    ClientSecret = FakeOidcProvider.ClientSecret,
                    DisplayName = name == Staff ? "Staff" : "Workspace",
                    DefaultRole = BuiltInRoles.AuthorName,
                };
            }
        });
        using var browser = new HostedPagesBrowser(host, "10.7.9.1");

        var admin = await browser.PageAsync("/account/login");
        admin.ShouldContain("Sign in with Workspace");
        admin.ShouldContain("Sign in with Staff");
        admin.ShouldNotContain("Continue with");
        admin.ShouldNotContain("member-");
        admin.ShouldNotContain("Corp");

        var members = await browser.PageAsync("/account/members/login");
        var register = await browser.PageAsync("/account/members/register");
        foreach (var page in new[] { members, register })
        {
            page.ShouldContain("Continue with Google");
            page.ShouldContain("Continue with Corp");
            page.ShouldNotContain("Workspace");
            page.ShouldNotContain("Staff");
        }

        // The two schemes of the one name are apart, and only the admin's has a name the admin login page lists.
        var schemes = host.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var staffScheme = (await schemes.GetSchemeAsync(Google)).ShouldNotBeNull();
        staffScheme.HandlerType.ShouldBe(typeof(OpenIdConnectHandler));
        var memberScheme = (await schemes.GetSchemeAsync("member-google")).ShouldNotBeNull();
        memberScheme.HandlerType.ShouldBe(typeof(MemberGoogleHandler));
        memberScheme.DisplayName.ShouldBeNull();

        // Neither page starts a sign-in at a provider of the other pool, by its name or by its scheme.
        foreach (var name in new[] { "member-google", Corp, "member-corp", MemberProviders.ExternalScheme })
        {
            using var refused = await browser.PostAsync("/account/login?handler=External", admin, new Dictionary<string, string> { ["provider"] = name });
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, name);
        }

        using (var refused = await browser.PostAsync("/account/members/login?handler=External", members, new Dictionary<string, string> { ["provider"] = Staff }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // The name both pools have goes to each pool's own provider.
        using (var staff = await browser.PostAsync("/account/login?handler=External", admin, new Dictionary<string, string> { ["provider"] = Google }))
        {
            staff.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe(fixture.Oidc.Issuer + "/authorize");
            QueryHelpers.ParseQuery(staff.Headers.Location.Query)["redirect_uri"].ToString().ShouldBe("https://localhost/account/sso/google");

            // An admin sign-in at the provider is in the admin's external cookie, which the member page does not read.
            var answer = fixture.Oidc.Authorize(staff.Headers.Location, FakeIdentity.Of(MemberFixture.NewAddress()));
            using var back = await browser.PostAsync("/account/sso/google", answer);
            back.Headers.Location!.OriginalString.ShouldStartWith("/account/external-login?");
            using var misused = await browser.GetAsync("/account/members/external-login");
            misused.Headers.Location!.OriginalString.ShouldBe("/account/members/login?ReturnUrl=%2F&Status=external-failed");
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        // A member sign-in at a provider is in the member's external cookie, which the admin page does not read.
        var address = MemberFixture.NewAddress();
        using var visitor = new HostedPagesBrowser(host, "10.7.9.2");
        using (var back = await SignInWithAsync(visitor, Google, FakeAccount.Of(address)))
        {
            visitor.HasCookie(ExternalCookie).ShouldBeTrue();
            using var misused = await visitor.GetAsync("/account/external-login");
            misused.Headers.Location!.OriginalString.ShouldBe("/account/login?ReturnUrl=%2F&Status=external-failed");
            visitor.HasCookie(AdminCookie).ShouldBeFalse();
            await using (var scope = host.Services.CreateAsyncScope())
            {
                (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByEmailAsync(address)).ShouldBeNull();
            }

            (await DecideAsync(visitor, back)).ShouldBe("/");
        }

        visitor.HasCookie(MemberCookie).ShouldBeTrue();
        visitor.HasCookie(AdminCookie).ShouldBeFalse();
        (await MemberAsync(host, address)).ShouldNotBeNull();
    }

    [Fact]
    public async Task AProviderErrorOrACallbackOfNoSignInStartedHereGoesBackToTheMemberLoginPageAsAFailedSignIn()
    {
        var redeemed = fixture.Providers.TokenRequests.Count;
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid");

        // The visitor refuses at the provider.
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.10.1"))
        {
            using var back = await SignInWithAsync(browser, Google, FakeAccount.Of(MemberFixture.NewAddress()), request.Url, error: "access_denied");
            back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            var login = back.Headers.Location!.OriginalString;
            login.ShouldStartWith("/account/members/login?");
            var query = QueryHelpers.ParseQuery(login[login.IndexOf('?', StringComparison.Ordinal)..]);
            query["Status"].ToString().ShouldBe("external-failed");
            query["ReturnUrl"].ToString().ShouldBe(request.Url);
            (await browser.PageAsync(login)).ShouldContain("did not complete");
            browser.HasCookie(MemberCookie).ShouldBeFalse();
            browser.HasCookie(ExternalCookie).ShouldBeFalse();
        }

        var failed = (await MemberFixture.ActivityAsync(fixture.Host, "member.login_failed")).Single(row => row.Ip == "10.7.10.1");
        failed.TargetId.ShouldBeNull();
        Details(failed)["method"].ShouldBe(Google);
        Details(failed)["reason"].ShouldBe(LoginFailureReasons.InvalidCredentials);

        // An answer of the provider that another browser asked for: the state is of a sign-in this browser never started.
        foreach (var provider in new[] { Google, MicrosoftAccount, GitHub, Apple, Corp })
        {
            var account = FakeAccount.Of(MemberFixture.NewAddress()) with { Addresses = [(MemberFixture.NewAddress(), true, true)] };
            using var victim = new HostedPagesBrowser(fixture.Host, "10.7.10.2");
            using var attacker = new HostedPagesBrowser(fixture.Host, "10.7.10.3");
            var authorize = await StartAsync(attacker, provider);

            using var back = await AnswerAsync(victim, provider, authorize, account);
            back.StatusCode.ShouldBe(HttpStatusCode.Redirect, provider);
            back.Headers.Location!.OriginalString.ShouldBe("/account/members/login?ReturnUrl=%2F&Status=external-failed", provider);
            victim.HasCookie(MemberCookie).ShouldBeFalse();
            victim.HasCookie(ExternalCookie).ShouldBeFalse();
            if (account.Email is not null)
            {
                (await MemberAsync(fixture.Host, account.Email)).ShouldBeNull();
            }
        }

        // A callback with a state nobody made.
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.10.4"))
        {
            using var back = await browser.GetAsync("/account/members/sso/google?code=made-up&state=made-up");
            back.Headers.Location!.OriginalString.ShouldBe("/account/members/login?Status=external-failed");
        }

        // No code was redeemed for any of them.
        fixture.Providers.TokenRequests.Count.ShouldBe(redeemed);
        (await MemberFixture.ActivityAsync(fixture.Host, "member.login_failed")).Count(row => row.Ip is "10.7.10.2" or "10.7.10.4").ShouldBe(6);
    }

    [Fact]
    public async Task ADeactivatedOrLockedOutMemberIsRefusedOnAProviderSignIn()
    {
        var account = FakeAccount.Of(MemberFixture.NewAddress());
        using (var first = new HostedPagesBrowser(fixture.Host, "10.7.11.1"))
        {
            using var back = await SignInWithAsync(first, Google, account);
            (await DecideAsync(first, back)).ShouldBe("/");
        }

        var member = (await MemberAsync(fixture.Host, account.Email!)).ShouldNotBeNull();
        var id = member.Id.ToString("D");

        await ChangeAsync(id, (members, found) => members.SetLockoutEndDateAsync(found, DateTimeOffset.MaxValue));
        (await RefusedPageAsync("10.7.11.2")).ShouldContain(Refused);
        await ChangeAsync(id, (members, found) => members.SetLockoutEndDateAsync(found, null));

        await ChangeAsync(id, (members, found) => members.DeactivateAsync(found));
        (await RefusedPageAsync("10.7.11.3")).ShouldContain(Refused);

        var rows = await MemberFixture.ActivityAsync(fixture.Host, "member.login_failed", id);
        rows.Select(row => (row.Ip, Details(row)["method"], Details(row)["reason"])).ShouldBe(
            [("10.7.11.2", Google, LoginFailureReasons.LockedOut), ("10.7.11.3", Google, LoginFailureReasons.NotAllowed)],
            ignoreOrder: true);

        async Task<string> RefusedPageAsync(string address)
        {
            using var browser = new HostedPagesBrowser(fixture.Host, address);
            using var back = await SignInWithAsync(browser, Google, account);
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
            browser.HasCookie(ExternalCookie).ShouldBeFalse();
            return await page.Content.ReadAsStringAsync(Token);
        }
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task AfterAProviderSignInTheBrowserStaysOnThisHost(string returnUrl)
    {
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.12.1");

        using var back = await SignInWithAsync(browser, Google, FakeAccount.Of(MemberFixture.NewAddress()), returnUrl);

        (await DecideAsync(browser, back)).ShouldBe("/");
        browser.HasCookie(MemberCookie).ShouldBeTrue();
    }

    // The callback a provider is given, and the one the code is redeemed with, are on the public origin whatever host
    // the request names.
    [Theory]
    [InlineData(Google)]
    [InlineData(GitHub)]
    [InlineData(Apple)]
    [InlineData(Corp)]
    public async Task TheRedirectUriOfAProviderIsBuiltFromThePublicOriginAndNotFromTheRequest(string provider)
    {
        var address = MemberFixture.NewAddress();
        var account = FakeAccount.Of(provider == GitHub ? null : address) with { Addresses = [(address, true, true)] };
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.13.1", new Uri("https://forged.example/"));

        var authorize = await StartAsync(browser, provider);
        QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString().ShouldBe($"https://localhost/account/members/sso/{provider}");

        // The provider redeems the code only with the callback it was given, so the member is there only if both agree.
        using var back = await AnswerAsync(browser, provider, authorize, account);
        (await DecideAsync(browser, back)).ShouldBe("/");
        (await MemberAsync(fixture.Host, address)).ShouldNotBeNull();
    }

    // Without a public origin the callback follows the request, and under a path base every step carries it.
    [Fact]
    public async Task UnderAPathBaseTheCallbackTheDecisionAndTheWayBackCarryIt()
    {
        await using var host = NewHost(fixture.Providers, fixture.Oidc, fixture.Emails, configure: cms =>
        {
            cms.PublicOrigin = null;
            cms.ReverseProxy.PathBase = "/cms";
        });
        const string Login = "/cms/account/members/login";

        foreach (var provider in new[] { Google, Corp })
        {
            var address = MemberFixture.NewAddress();
            using var browser = new HostedPagesBrowser(host, "10.7.16.1");
            using var back = await AnswerAsync(browser, provider, await StartAsync(browser, provider, page: Login), FakeAccount.Of(address), pathBase: "/cms");
            back.Headers.Location!.OriginalString.ShouldBe("/cms/account/members/external-login?ReturnUrl=%2Fcms%2F");
            using var decided = await browser.GetAsync(back.Headers.Location.OriginalString);
            decided.Headers.Location!.OriginalString.ShouldBe("/cms/");
            (await MemberAsync(host, address)).ShouldNotBeNull();

            using var denied = await AnswerAsync(browser, provider, await StartAsync(browser, provider, page: Login), FakeAccount.Of(address), error: "access_denied", pathBase: "/cms");
            denied.Headers.Location!.OriginalString.ShouldBe("/cms/account/members/login?ReturnUrl=%2Fcms%2F&Status=external-failed");
        }

        using var visitor = new HostedPagesBrowser(host, "10.7.16.2");
        using var logins = await visitor.GetAsync("/cms/account/members/logins");
        logins.Headers.Location!.OriginalString.ShouldBe("/cms/account/members/login?ReturnUrl=%2Fcms%2Faccount%2Fmembers%2Flogins");
    }

    // A provider that did not verify the address makes a member whose login nobody has shown to be the mailbox owner's.
    // Once the owner registers the address, or chooses a password with a mailed link, that login is gone. The host is
    // the test's own, since it moves the clock past the minute between two mails to one address.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALoginOfAnAddressTheProviderDidNotVerifyDoesNotOutliveTheOwnerOfTheMailboxChoosingAPassword(bool ownerRegisters)
    {
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = NewHost(fixture.Providers, fixture.Oidc, emails);
        var address = MemberFixture.NewAddress();
        var stranger = new FakeAccount(Guid.NewGuid().ToString("D"), address);
        using (var browser = new HostedPagesBrowser(host, "10.7.14.1"))
        {
            using var back = await SignInWithAsync(browser, MicrosoftAccount, stranger);
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
        }

        var member = (await MemberAsync(host, address)).ShouldNotBeNull();
        (await LoginsAsync(host, member.Id)).ShouldHaveSingleItem();
        var strangersLink = emails.LinkTo(address);
        strangersLink.AbsolutePath.ShouldBe("/account/members/verify-email");

        // Whoever asks for the verification mail again gets the one that names the provider its link lets in.
        using var client = host.CreateSecureClient();
        host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "resend-verification", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        emails.Sent.Count(mail => mail.To == address && mail.TextBody.Contains("Someone signed in with Microsoft", StringComparison.Ordinal)).ShouldBe(2);

        host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        if (ownerRegisters)
        {
            // The owner registers the address: the login goes at once, and so does the link the stranger's sign-in sent.
            var registered = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{address}}", "password": "{{MemberFixture.Password}}" }""");
            registered.Status.ShouldBe(HttpStatusCode.Accepted);
            (await LoginsAsync(host, member.Id)).ShouldBeEmpty();
            var spent = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "verify-email", $$"""{ "email": "{{address}}", "token": "{{TokenOf(strangersLink)}}" }""");
            spent.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
        else
        {
            (await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "forgot-password", $$"""{ "email": "{{address}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
            (await LoginsAsync(host, member.Id)).ShouldHaveSingleItem();
        }

        // The owner chooses a password with the link of the mail, which verifies the address too.
        var link = emails.LinkTo(address);
        link.AbsolutePath.ShouldBe("/account/members/reset-password");
        var reset = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "reset-password", $$"""{ "email": "{{address}}", "token": "{{TokenOf(link)}}", "password": "{{MemberFixture.Password}}" }""");
        reset.Status.ShouldBe(HttpStatusCode.NoContent);

        var owned = (await MemberAsync(host, address)).ShouldNotBeNull();
        owned.Id.ShouldBe(member.Id);
        owned.EmailConfirmed.ShouldBeTrue();
        (await LoginsAsync(host, member.Id)).ShouldBeEmpty();
        Details((await MemberFixture.ActivityAsync(host, "member.login_unlinked", member.Id.ToString("D"))).ShouldHaveSingleItem())["provider"].ShouldBe(MicrosoftAccount);

        // The stranger's account at the provider signs nobody in, and the owner signs in with the password.
        using (var browser = new HostedPagesBrowser(host, "10.7.14.2"))
        {
            using var back = await SignInWithAsync(browser, MicrosoftAccount, stranger);
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
            await SignInWithPasswordAsync(browser, address);
            browser.HasCookie(MemberCookie).ShouldBeTrue();
        }

        (await LoginsAsync(host, member.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task NoLogLineHoldsASecretOfAProviderOrAnAddress()
    {
        var address = MemberFixture.NewAddress();
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.15.1"))
        {
            using var apple = await SignInWithAsync(browser, Apple, new FakeAccount("001234." + Guid.NewGuid().ToString("N"), address));
            (await DecideAsync(browser, apple)).ShouldBe("/");
            using var taken = await SignInWithAsync(browser, Google, FakeAccount.Of(address));
            using var refused = await browser.FollowAsync(taken);
            using var denied = await SignInWithAsync(browser, Corp, FakeAccount.Of(MemberFixture.NewAddress()), error: "access_denied");
        }

        var key = string.Concat(fixture.Providers.ApplePrivateKey.Split('\n').Where(line => !line.StartsWith("-----", StringComparison.Ordinal)).Select(line => line.Trim()));
        var lines = fixture.Logs.Entries;
        lines.ShouldContain(entry => entry.EventId.Id == MemberLog.ProviderMemberCreatedId);
        lines.ShouldContain(entry => entry.EventId.Id == MemberLog.ProviderSignInRefusedId);
        lines.ShouldContain(entry => entry.EventId.Id == MemberLog.ProviderFailureId);

        // What FluentCMS writes, at any level, and what anything writes at the levels a host keeps.
        foreach (var entry in lines.Where(entry => entry.Level >= LogLevel.Information || entry.Category.StartsWith("FluentCMS", StringComparison.Ordinal)))
        {
            var text = entry.Message + string.Join(' ', entry.State.Values) + entry.Exception;
            text.ShouldNotContain(fixture.Providers.ClientSecret);
            text.ShouldNotContain(FakeOidcProvider.ClientSecret);
            text.ShouldNotContain(key[..40]);
            if (entry.Category.StartsWith("FluentCMS.Core.Identity.Members", StringComparison.Ordinal))
            {
                text.ShouldNotContain(address);
            }
        }
    }

    private async Task ChangeAsync(string memberId, Func<MemberManager, CmsMember, Task> change)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var members = scope.ServiceProvider.GetRequiredService<MemberManager>();
        await change(members, (await members.FindByIdAsync(memberId))!);
    }

    private static string TokenOf(Uri link) => QueryHelpers.ParseQuery(link.Query)["token"].ToString();
}
