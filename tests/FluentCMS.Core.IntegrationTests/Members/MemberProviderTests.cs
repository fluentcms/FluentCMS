using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Core.IntegrationTests.Members.MemberProviderFixture;
using AuthorizeRequest = FluentCMS.Core.IntegrationTests.Members.MemberPagesTests.AuthorizeRequest;

namespace FluentCMS.Core.IntegrationTests.Members;

// Member sign-in through Google, Microsoft, GitHub, Apple and generic OIDC (#2993), driven the way a browser drives the
// member pages, against providers in the process.
public sealed partial class MemberProviderTests(MemberProviderFixture fixture) : IClassFixture<MemberProviderFixture>
{
    private const string MemberCookie = ".FluentCms.Members";
    private const string ExternalCookie = ".FluentCms.Members.External";
    private const string AdminCookie = ".AspNetCore.Identity.Application";
    private const string Refused = "did not sign you in";
    private const string CheckYourMail = "an email is on its way";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewVisitorSignsInWithGoogleBecomesAVerifiedMemberAndGetsTokensThroughTheCodeFlow()
    {
        var address = MemberFixture.NewAddress();
        var account = FakeAccount.Of(address) with { Name = "Gina Google" };
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.0.1");
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid profile email offline_access");

        // The member client's request goes to the member login page, which offers the provider.
        using var challenge = await browser.GetAsync(request.Url);
        var login = challenge.Headers.Location!;
        login.AbsolutePath.ShouldBe("/account/members/login");
        (await browser.PageAsync(login.PathAndQuery)).ShouldContain("Continue with Google");

        var authorize = await StartAsync(browser, Google, request.Url);
        authorize.GetLeftPart(UriPartial.Path).ShouldBe("https://accounts.google.com/o/oauth2/v2/auth");
        var asked = QueryHelpers.ParseQuery(authorize.Query);
        asked["scope"].ToString().Split(' ').ShouldBe(["openid", "profile", "email"], ignoreOrder: true);
        asked["code_challenge_method"].ToString().ShouldBe("S256");

        using var callback = await AnswerAsync(browser, Google, authorize, account);
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect, await callback.Content.ReadAsStringAsync(Token));
        callback.Headers.Location!.OriginalString.ShouldStartWith("/account/members/external-login?");
        using var done = await browser.FollowAsync(callback);
        var tokens = await request.ExchangeAsync(fixture.Host, done);

        // The sign-in is the member's alone, and the cookie the provider signed into is spent.
        browser.HasCookie(MemberCookie).ShouldBeTrue();
        browser.HasCookie(ExternalCookie).ShouldBeFalse();
        browser.HasCookie(AdminCookie).ShouldBeFalse();

        var access = MemberTokenTests.Payload(tokens.GetProperty("access_token").GetString()!);
        MemberTokenTests.Audiences(access).ShouldBe(["fluentcms:members"]);
        access.GetProperty("fcms_kind").GetString().ShouldBe("Member");
        access.GetProperty("name").GetString().ShouldBe("Gina Google");
        tokens.GetProperty("refresh_token").GetString().ShouldNotBeNullOrEmpty();

        var member = (await MemberAsync(fixture.Host, address)).ShouldNotBeNull();
        member.EmailConfirmed.ShouldBeTrue();
        member.PasswordHash.ShouldBeNull();
        member.DisplayName.ShouldBe("Gina Google");
        access.GetProperty("sub").GetString().ShouldBe(member.Id.ToString("D"));
        var linked = (await LoginsAsync(fixture.Host, member.Id)).ShouldHaveSingleItem();
        (linked.LoginProvider, linked.ProviderKey, linked.ProviderDisplayName).ShouldBe((Google, account.Subject, "Google"));

        // The code was redeemed with the callback of the public origin and the secret of the configuration.
        fixture.Providers.TokenRequests.ShouldContain(new TokenRequest("google", "https://localhost/account/members/sso/google", fixture.Providers.ClientSecret));

        var rows = await MemberFixture.ActivityAsync(fixture.Host, "member.", member.Id.ToString("D"));
        rows.Select(row => row.Action).ShouldBe(["member.registered", "member.verified", "member.login_linked", "member.login_succeeded"], ignoreOrder: true);
        rows.ShouldAllBe(row => row.ActorType == PrincipalKind.Member && row.ActorId == member.Id);
        Details(rows.Single(row => row.Action == "member.login_linked"))["provider"].ShouldBe(Google);
        var signedIn = rows.Single(row => row.Action == "member.login_succeeded");
        Details(signedIn)["method"].ShouldBe(Google);
        signedIn.Ip.ShouldBe("10.7.0.1");

        // The second sign-in finds the member the first one made.
        using (var again = new HostedPagesBrowser(fixture.Host, "10.7.0.1"))
        {
            using var back = await SignInWithAsync(again, Google, account);
            (await DecideAsync(again, back)).ShouldBe("/");
            again.HasCookie(MemberCookie).ShouldBeTrue();
        }

        (await MemberAsync(fixture.Host, address))!.Id.ShouldBe(member.Id);

        // A member a provider made has no password, and the direct login grant takes no external login.
        using var direct = await browser.PostAsync("/connect/token", new Dictionary<string, string>
        {
            ["grant_type"] = "member_password",
            ["client_id"] = MemberFixture.DirectClientId,
            ["username"] = address,
            ["password"] = account.Subject,
            ["scope"] = "openid",
        });
        direct.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await direct.Content.ReadAsStringAsync(Token)).ShouldContain("invalid_grant");
    }

    [Fact]
    public async Task AMemberLinkedToAProviderSignsInWithItAndAVisitorIsRefusedWhileRegistrationIsOff()
    {
        await using var host = NewHost(fixture.Providers, fixture.Oidc, fixture.Emails, configure: cms => cms.Members.AllowRegistration = false);
        var known = await host.CreateMemberAsync(cancellationToken: Token);
        var account = FakeAccount.Of("another-address-" + known.Email);
        await LinkAsync(host, known.Id, Google, account.Subject);

        // The page that registers is not there, and the login page still offers the provider.
        using var probe = new HostedPagesBrowser(host, "10.7.1.1");
        using (var register = await probe.GetAsync("/account/members/register"))
        {
            register.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await probe.PageAsync("/account/members/login")).ShouldContain("Continue with Google");

        // The login is what names the member: the address the provider gives is another one.
        using (var browser = new HostedPagesBrowser(host, "10.7.1.2"))
        {
            var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid");
            using var back = await SignInWithAsync(browser, Google, account, request.Url);
            var tokens = await request.ExchangeAsync(host, await browser.FollowAsync(back));
            MemberTokenTests.Payload(tokens.GetProperty("access_token").GetString()!).GetProperty("sub").GetString().ShouldBe(known.Id.ToString("D"));
        }

        (await MemberAsync(host, account.Email!)).ShouldBeNull();

        // A visitor with no member is refused, and none is made.
        var stranger = FakeAccount.Of(MemberFixture.NewAddress());
        using (var browser = new HostedPagesBrowser(host, "10.7.1.3"))
        {
            using var back = await SignInWithAsync(browser, Google, stranger);
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(Refused);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
            browser.HasCookie(ExternalCookie).ShouldBeFalse();
        }

        (await MemberAsync(host, stranger.Email!)).ShouldBeNull();
        var failed = (await MemberFixture.ActivityAsync(host, "member.login_failed")).Single(row => row.Ip == "10.7.1.3");
        failed.TargetId.ShouldBeNull();
        Details(failed)["method"].ShouldBe(Google);
        Details(failed)["reason"].ShouldBe(LoginFailureReasons.UnknownUser);
    }

    [Fact]
    public async Task AMicrosoftSignInOfANewVisitorCreatesAMemberThatVerifiesItsAddressByMailBeforeItGetsAToken()
    {
        var address = MemberFixture.NewAddress();
        var account = new FakeAccount(Guid.NewGuid().ToString("D"), address) { Name = "Mia Microsoft" };
        using var browser = new HostedPagesBrowser(fixture.Host, "10.7.2.1");
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid profile");

        // Microsoft says nothing of the address, so nobody is signed in: the member is made and the mail goes.
        using (var back = await SignInWithAsync(browser, MicrosoftAccount, account, request.Url))
        {
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
        }

        browser.HasCookie(MemberCookie).ShouldBeFalse();
        browser.HasCookie(ExternalCookie).ShouldBeFalse();
        var member = (await MemberAsync(fixture.Host, address)).ShouldNotBeNull();
        member.EmailConfirmed.ShouldBeFalse();
        member.PasswordHash.ShouldBeNull();
        (await LoginsAsync(fixture.Host, member.Id)).ShouldHaveSingleItem().LoginProvider.ShouldBe(MicrosoftAccount);
        (await LocationAsync(browser, request.Url)).ShouldContain("/account/members/login");

        // Signing in again before the address is verified changes nothing.
        using (var back = await SignInWithAsync(browser, MicrosoftAccount, account, request.Url))
        {
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        // The mail is the verification mail of the pool, and names the provider its link lets in; once the link is
        // used, the provider signs the member in.
        fixture.Emails.Sent.Last(mail => mail.To == address).TextBody.ShouldContain("Someone signed in with Microsoft");
        var link = fixture.Emails.LinkTo(address);
        link.AbsolutePath.ShouldBe("/account/members/verify-email");
        using (var verified = await browser.SubmitAsync(link.PathAndQuery, new Dictionary<string, string>()))
        {
            verified.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        using var signedIn = await SignInWithAsync(browser, MicrosoftAccount, account, request.Url);
        var tokens = await request.ExchangeAsync(fixture.Host, await browser.FollowAsync(signedIn));
        var access = MemberTokenTests.Payload(tokens.GetProperty("access_token").GetString()!);
        access.GetProperty("sub").GetString().ShouldBe(member.Id.ToString("D"));
        access.GetProperty("name").GetString().ShouldBe("Mia Microsoft");

        var rows = await MemberFixture.ActivityAsync(fixture.Host, "member.", member.Id.ToString("D"));
        rows.Count(row => row.Action == "member.login_failed" && Details(row)["method"] == MicrosoftAccount && Details(row)["reason"] == LoginFailureReasons.NotAllowed).ShouldBe(2);
        rows.Select(row => row.Action).Where(action => action != "member.login_failed")
            .ShouldBe(["member.registered", "member.login_linked", "member.verified", "member.login_succeeded"], ignoreOrder: true);
    }

    [Fact]
    public async Task AGitHubSignInUsesTheVerifiedPrimaryAddressAndOneWithoutAVerifiedAddressCreatesNoMember()
    {
        var (primary, other, shown) = (MemberFixture.NewAddress(), MemberFixture.NewAddress(), MemberFixture.NewAddress());
        var account = FakeAccount.Of(null) with
        {
            Name = "Hub Member",
            Login = "hub-member",
            PublicEmail = shown,
            Addresses = [(other, false, true), (primary, true, true)],
        };

        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.3.1"))
        {
            var authorize = await StartAsync(browser, GitHub);
            authorize.GetLeftPart(UriPartial.Path).ShouldBe("https://github.com/login/oauth/authorize");
            QueryHelpers.ParseQuery(authorize.Query)["scope"].ToString().ShouldContain("user:email");

            using var back = await AnswerAsync(browser, GitHub, authorize, account);
            (await DecideAsync(browser, back)).ShouldBe("/");
        }

        // Neither the address the profile shows nor a verified one that is not primary names the member.
        var member = (await MemberAsync(fixture.Host, primary)).ShouldNotBeNull();
        member.EmailConfirmed.ShouldBeTrue();
        member.DisplayName.ShouldBe("Hub Member");
        (await MemberAsync(fixture.Host, other)).ShouldBeNull();
        (await MemberAsync(fixture.Host, shown)).ShouldBeNull();
        (await LoginsAsync(fixture.Host, member.Id)).ShouldHaveSingleItem().ProviderKey.ShouldBe(account.Subject);

        // A primary address GitHub has not verified is no address, whatever else the account shows.
        var (unverified, secondary) = (MemberFixture.NewAddress(), MemberFixture.NewAddress());
        var doubtful = FakeAccount.Of(null) with { PublicEmail = secondary, Addresses = [(unverified, true, false), (secondary, false, true)] };
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.3.2"))
        {
            using var back = await SignInWithAsync(browser, GitHub, doubtful);
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(Refused);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        (await MemberAsync(fixture.Host, unverified)).ShouldBeNull();
        (await MemberAsync(fixture.Host, secondary)).ShouldBeNull();
        var failed = (await MemberFixture.ActivityAsync(fixture.Host, "member.login_failed")).Single(row => row.Ip == "10.7.3.2");
        failed.TargetId.ShouldBeNull();
        Details(failed)["method"].ShouldBe(GitHub);
        fixture.Logs.Entries.ShouldContain(entry => entry.EventId.Id == MemberLog.ProviderSignInRefusedId && entry.Message.StartsWith(MemberErrorCodes.ProviderNoEmail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAppleProviderAndAGenericOidcProviderSignAMemberIn()
    {
        // Apple: the handler makes the client secret from the private key, which the provider checks, and the member's
        // address and its flag come from the id token.
        var appleAddress = MemberFixture.NewAddress();
        var apple = new FakeAccount("001234." + Guid.NewGuid().ToString("N"), appleAddress);
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.4.1"))
        {
            var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid email");
            var authorize = await StartAsync(browser, Apple, request.Url);
            authorize.GetLeftPart(UriPartial.Path).ShouldBe("https://appleid.apple.com/auth/authorize");

            using var back = await AnswerAsync(browser, Apple, authorize, apple);
            var tokens = await request.ExchangeAsync(fixture.Host, await browser.FollowAsync(back));
            tokens.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
        }

        var appleMember = (await MemberAsync(fixture.Host, appleAddress)).ShouldNotBeNull();
        appleMember.EmailConfirmed.ShouldBeTrue();
        (await LoginsAsync(fixture.Host, appleMember.Id)).ShouldHaveSingleItem().ProviderKey.ShouldBe(apple.Subject);
        var redeemed = fixture.Providers.TokenRequests.Last(sent => sent.Kind == "apple");
        redeemed.RedirectUri.ShouldBe("https://localhost/account/members/sso/apple");
        redeemed.ClientSecret.ShouldNotBeNull().Split('.').Length.ShouldBe(3);
        fixture.Providers.Unknown.ShouldBeEmpty();

        // An Apple account whose address Apple has not verified makes a member that verifies it by mail.
        var unverifiedAddress = MemberFixture.NewAddress();
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.4.2"))
        {
            using var back = await SignInWithAsync(browser, Apple, new FakeAccount("001234." + Guid.NewGuid().ToString("N"), unverifiedAddress) { EmailVerified = false });
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
        }

        (await MemberAsync(fixture.Host, unverifiedAddress))!.EmailConfirmed.ShouldBeFalse();

        // Generic OIDC: the code flow with PKCE, and email_verified of the id token.
        var corpAddress = MemberFixture.NewAddress();
        var corp = FakeAccount.Of(corpAddress);
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.4.3"))
        {
            var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid");
            (await browser.PageAsync("/account/members/login")).ShouldContain("Continue with Corp");
            using var back = await SignInWithAsync(browser, Corp, corp, request.Url);
            var tokens = await request.ExchangeAsync(fixture.Host, await browser.FollowAsync(back));
            tokens.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
        }

        var corpMember = (await MemberAsync(fixture.Host, corpAddress)).ShouldNotBeNull();
        corpMember.EmailConfirmed.ShouldBeTrue();
        var corpLogin = (await LoginsAsync(fixture.Host, corpMember.Id)).ShouldHaveSingleItem();
        (corpLogin.LoginProvider, corpLogin.ProviderKey, corpLogin.ProviderDisplayName).ShouldBe((Corp, corp.Subject, "Corp"));

        // A token that does not say the address is verified makes no verified member.
        var silentAddress = MemberFixture.NewAddress();
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.4.4"))
        {
            using var back = await SignInWithAsync(browser, Corp, FakeAccount.Of(silentAddress) with { EmailVerified = null });
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        (await MemberAsync(fixture.Host, silentAddress))!.EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAddressOfAnExistingMemberIsNotTakenOverAndLinkVerifiedEmailLinksOnlyAnAddressTheProviderVerified()
    {
        var existing = await fixture.Host.CreateMemberAsync(password: MemberFixture.Password, cancellationToken: Token);

        // Even an address Google verified: the member is not linked to the login, so the sign-in is refused.
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.5.1"))
        {
            using var back = await SignInWithAsync(browser, Google, FakeAccount.Of(existing.Email));
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(Refused);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        (await LoginsAsync(fixture.Host, existing.Id)).ShouldBeEmpty();
        var refused = (await MemberFixture.ActivityAsync(fixture.Host, "member.login_failed")).Single(row => row.Ip == "10.7.5.1");
        refused.TargetId.ShouldBeNull();
        Details(refused)["method"].ShouldBe(Google);
        Details(refused)["reason"].ShouldBe(LoginFailureReasons.NotAllowed);
        fixture.Logs.Entries.ShouldContain(entry => entry.EventId.Id == MemberLog.ProviderSignInRefusedId && entry.Message.StartsWith(MemberErrorCodes.ProviderEmailTaken, StringComparison.Ordinal));

        // Microsoft never says an address is verified: nothing is linked, and the answer is the one a new address gets.
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.5.2"))
        {
            using var back = await SignInWithAsync(browser, MicrosoftAccount, new FakeAccount(Guid.NewGuid().ToString("D"), existing.Email));
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        (await LoginsAsync(fixture.Host, existing.Id)).ShouldBeEmpty();
        fixture.Emails.Sent.Last(mail => mail.To == existing.Email).Subject.ShouldBe("You already have an account");

        // The provider that links: not while it says the address is not verified, or says nothing of it.
        foreach (var (verified, address) in new (bool?, string)[] { (false, "10.7.5.3"), (null, "10.7.5.4") })
        {
            using var browser = new HostedPagesBrowser(fixture.Host, address);
            using var back = await SignInWithAsync(browser, Partner, FakeAccount.Of(existing.Email) with { EmailVerified = verified });
            using var page = await browser.FollowAsync(back);
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        (await LoginsAsync(fixture.Host, existing.Id)).ShouldBeEmpty();

        // It links when it says the address is verified, and the member is signed in.
        var partner = FakeAccount.Of(existing.Email);
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.5.5"))
        {
            using var back = await SignInWithAsync(browser, Partner, partner);
            (await DecideAsync(browser, back)).ShouldBe("/");
            browser.HasCookie(MemberCookie).ShouldBeTrue();
        }

        var login = (await LoginsAsync(fixture.Host, existing.Id)).ShouldHaveSingleItem();
        (login.LoginProvider, login.ProviderKey).ShouldBe((Partner, partner.Subject));
        var rows = await MemberFixture.ActivityAsync(fixture.Host, "member.login_", existing.Id.ToString("D"));
        Details(rows.Single(row => row.Action == "member.login_linked"))["provider"].ShouldBe(Partner);
        Details(rows.Single(row => row.Action == "member.login_succeeded"))["method"].ShouldBe(Partner);

        // A second account at the provider with the same address does not join the first.
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.5.6"))
        {
            using var back = await SignInWithAsync(browser, Partner, FakeAccount.Of(existing.Email));
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(Refused);
        }

        (await LoginsAsync(fixture.Host, existing.Id)).ShouldHaveSingleItem();

        // A member whose address is not verified is not linked either, even by this provider: its password is not shown
        // to be its owner's, so the address is contested and nobody is signed in.
        var pending = MemberFixture.NewAddress();
        await RegisterAsync(pending);
        using (var browser = new HostedPagesBrowser(fixture.Host, "10.7.5.7"))
        {
            using var back = await SignInWithAsync(browser, Partner, FakeAccount.Of(pending));
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain(CheckYourMail);
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        var unverified = (await MemberAsync(fixture.Host, pending)).ShouldNotBeNull();
        unverified.EmailConfirmed.ShouldBeFalse();
        unverified.PasswordHash.ShouldBeNull();
        (await LoginsAsync(fixture.Host, unverified.Id)).ShouldBeEmpty();
    }

    // What the browser does: the button of a member page for the provider, which answers with the provider's authorize URL.
    private static async Task<Uri> StartAsync(HostedPagesBrowser browser, string provider, string? returnUrl = null, string page = "/account/members/login")
    {
        var html = await browser.PageAsync(page + (returnUrl is null ? "" : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl)));
        using var challenge = await browser.PostAsync(page + "?handler=External", html, new Dictionary<string, string> { ["provider"] = provider, ["ReturnUrl"] = returnUrl ?? "" });
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync(Token));
        return challenge.Headers.Location!;
    }

    // The visit to the provider and its answer back to the callback path: a form post from Apple and the OIDC provider,
    // a redirect from the others. Returns the callback's answer.
    private async Task<HttpResponseMessage> AnswerAsync(HostedPagesBrowser browser, string provider, Uri authorize, FakeAccount account, string? error = null, string pathBase = "")
    {
        var callback = new Uri(QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString());
        callback.AbsoluteUri.ShouldBe($"https://localhost{pathBase}/account/members/sso/{provider}");

        if (provider is Corp or Partner)
        {
            return await browser.PostAsync(callback.PathAndQuery, fixture.Oidc.Authorize(authorize, account.ToIdentity(), error));
        }

        var answer = fixture.Providers.Authorize(authorize, account, error);
        return provider == Apple
            ? await browser.PostAsync(callback.PathAndQuery, answer)
            : await browser.GetAsync(callback.PathAndQuery + QueryString.Create(answer.Select(pair => KeyValuePair.Create(pair.Key, (string?)pair.Value))));
    }

    private async Task<HttpResponseMessage> SignInWithAsync(HostedPagesBrowser browser, string provider, FakeAccount account, string? returnUrl = null, string? error = null) =>
        await AnswerAsync(browser, provider, await StartAsync(browser, provider, returnUrl), account, error);

    // Where the member external login page sends the browser that the provider's callback sent there.
    private static async Task<string> DecideAsync(HostedPagesBrowser browser, HttpResponseMessage callback)
    {
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect, await callback.Content.ReadAsStringAsync(Token));
        callback.Headers.Location!.OriginalString.ShouldStartWith("/account/members/external-login?");
        using var page = await browser.GetAsync(callback.Headers.Location.OriginalString);
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect, await page.Content.ReadAsStringAsync(Token));
        return page.Headers.Location!.OriginalString;
    }

    // Where an authorize request sends the browser: the client's redirect URI with a code, or a login page.
    private static async Task<string> LocationAsync(HostedPagesBrowser browser, string url)
    {
        using var response = await browser.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return response.Headers.Location!.OriginalString;
    }

    private static async Task LinkAsync(FluentCmsTestHost host, Guid memberId, string provider, string key)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var members = scope.ServiceProvider.GetRequiredService<MemberManager>();
        (await members.AddLoginAsync((await members.FindByIdAsync(memberId.ToString("D")))!, new UserLoginInfo(provider, key, provider))).Succeeded.ShouldBeTrue();
    }

    // A member that registered with a password and has not verified its address.
    private async Task RegisterAsync(string address)
    {
        using var client = fixture.Host.CreateSecureClient();
        var registered = await MemberFixture.SendAsync(client, HttpMethod.Post, MemberFixture.Api + "register", $$"""{ "email": "{{address}}", "password": "{{MemberFixture.Password}}" }""");
        registered.Status.ShouldBe(HttpStatusCode.Accepted);
    }

    private static Dictionary<string, string?> Details(ActivityRow row) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(row.Details ?? "{}")!;
}
