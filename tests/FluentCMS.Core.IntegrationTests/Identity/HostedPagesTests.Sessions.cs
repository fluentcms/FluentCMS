using System.Net;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Identity;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What ends or refuses a sign-in on the hosted pages (#2854).
public sealed partial class HostedPagesTests
{
    private const string SignInCookie = ".AspNetCore.Identity.Application";

    [Fact]
    public async Task TheRemoteSignOutOfAProviderEndsTheAdminSessionItMadeAndNoOther()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(Corp).SignOutScheme.ShouldBe(IdentityConstants.ApplicationScheme);

        using var sso = new HostedPagesBrowser(host);
        using (var back = await SignInWithProviderAsync(sso, idp, FakeIdentity.Of("rae@corp.example")))
        {
            (await ExternalLoginAsync(sso, back)).ShouldBe("/");
        }

        var editor = await CreateUserAsync(host);
        using var password = new HostedPagesBrowser(host);
        await SignInAsync(password, editor.Email);

        // What the provider's front-channel logout loads in the browser.
        foreach (var (browser, provider) in new[] { (sso, Trusted), (password, Corp) })
        {
            using var response = await browser.GetAsync($"/account/sso/{provider}/signout");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(SignInCookie).ShouldBeTrue();
        }

        using var signedOut = await sso.GetAsync($"/account/sso/{Corp}/signout");
        signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
        sso.HasCookie(SignInCookie).ShouldBeFalse();
    }

    [Fact]
    public async Task ATokenThatDoesNotSayTheAddressIsVerifiedCreatesNoUserUnlessTheProviderAllowsIt()
    {
        new SsoProvider().RequireVerifiedEmail.ShouldBeTrue();
        using var idp = new FakeOidcProvider();
        var quiet = FakeIdentity.Of("quiet@corp.example") with { EmailVerified = null };

        await using (var host = SsoHost(idp))
        {
            using var browser = new HostedPagesBrowser(host);
            using var back = await SignInWithProviderAsync(browser, idp, quiet);
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByEmailAsync(quiet.Email!)).ShouldBeNull();
        }

        await using var lenient = SsoHost(idp, provider => provider.RequireVerifiedEmail = false);
        using (var browser = new HostedPagesBrowser(lenient))
        {
            using var back = await SignInWithProviderAsync(browser, idp, quiet);
            (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        }

        (await LinkedUserAsync(lenient, Corp, quiet.Subject)).Email.ShouldBe(quiet.Email);

        // One that says the address is not verified never creates a user.
        using var refused = new HostedPagesBrowser(lenient);
        using var unverified = await SignInWithProviderAsync(refused, idp, FakeIdentity.Of("loud@corp.example") with { EmailVerified = false });
        using var answer = await refused.FollowAsync(unverified);
        (await answer.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
    }

    [Fact]
    public async Task AWrongRecoveryCodeCountsTowardsTheLockout()
    {
        await using var host = TwoFactorHost();
        var editor = await CreateUserAsync(host);
        var (_, codes) = await EnableTwoFactorAsync(host, editor);
        using var browser = new HostedPagesBrowser(host, "10.2.8.1");

        using var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
        var returnUrl = QueryHelpers.ParseQuery(new Uri(OAuthBase, password.Headers.Location!).Query)["ReturnUrl"].ToString();
        var page = "/account/recovery-code?ReturnUrl=" + Uri.EscapeDataString(returnUrl);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = "WRONG-CODE" });
            wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Locked out now: a right code signs no one in, and stays unspent.
        using var right = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = codes[0] });
        right.StatusCode.ShouldBe(HttpStatusCode.OK);
        browser.HasCookie(SignInCookie).ShouldBeFalse();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            var stored = (await users.FindByIdAsync(editor.Id.ToString()))!;
            (await users.IsLockedOutAsync(stored)).ShouldBeTrue();
            (await users.CountRecoveryCodesAsync(stored)).ShouldBe(TwoFactorService.RecoveryCodeCount);
        }

        var failures = await ActivityAsync(host, "user.login_failed", count: 6);
        failures.Count(row => Details(row)["method"] == LoginMethods.RecoveryCode && Details(row)["reason"] == LoginFailureReasons.InvalidCredentials).ShouldBe(4);
        failures.Count(row => Details(row)["method"] == LoginMethods.RecoveryCode && Details(row)["reason"] == LoginFailureReasons.LockedOut).ShouldBe(2);
    }

    [Fact]
    public async Task AUserWhoMayNotSignInHasThePasswordHashRunAsAnyOther()
    {
        var hasher = new CountingHasher();
        await using var host = NewHost(_emails).WithServices(services => services.AddSingleton<IPasswordHasher<CmsUser>>(hasher));
        var deactivated = await CreateUserAsync(host);
        var locked = await CreateUserAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.DeactivateAsync((await users.FindByIdAsync(deactivated.Id.ToString()))!)).Succeeded.ShouldBeTrue();
            (await users.SetLockoutEndDateAsync((await users.FindByIdAsync(locked.Id.ToString()))!, DateTimeOffset.MaxValue)).Succeeded.ShouldBeTrue();
        }

        using var browser = new HostedPagesBrowser(host);
        var page = await browser.PageAsync("/account/login");
        foreach (var name in new[] { deactivated.Email, locked.Email, "nobody@example.com" })
        {
            var before = hasher.Calls;
            using var refused = await browser.PostAsync("/account/login", page, Credentials(name, Password));
            refused.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(LoginModel.Refused);
            (hasher.Calls - before).ShouldBe(1, name);
        }
    }

    [Fact]
    public async Task TheTimeOfASignInOutlastsTheRenewalsOfItsCookie()
    {
        await using var host = TwoFactorHost();
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);
        await SignInAsync(browser, editor.Email);

        // Past the stamp check, so the next request renews the cookie with a new issue time.
        host.Time.Advance(TimeSpan.FromMinutes(31));
        using (var renewed = await browser.GetAsync("/account/logout"))
        {
            renewed.Headers.GetValues("Set-Cookie").ShouldContain(value => value.StartsWith(SignInCookie + "=", StringComparison.Ordinal) && !value.StartsWith(SignInCookie + "=;", StringComparison.Ordinal));
        }

        host.Time.Advance(TimeSpan.FromMinutes(40));
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var asked = await browser.GetAsync(request.Url + "&max_age=3600");
        asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = new Uri(OAuthBase, asked.Headers.Location!);
        login.AbsolutePath.ShouldBe("/account/login");
        browser.HasCookie(SignInCookie).ShouldBeFalse();

        // Signing in again answers max_age, and the request comes back without it.
        using var signedIn = await browser.SubmitAsync(login.PathAndQuery, Credentials(editor.Email, Password));
        signedIn.Headers.Location!.OriginalString.ShouldNotContain("max_age");
        using var done = await browser.FollowAsync(signedIn);
        (await request.ExchangeAsync(host, done)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
    }

    private sealed class CountingHasher : IPasswordHasher<CmsUser>
    {
        private readonly PasswordHasher<CmsUser> _inner = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string HashPassword(CmsUser user, string password)
        {
            Interlocked.Increment(ref _calls);
            return _inner.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(CmsUser user, string hashedPassword, string providedPassword)
        {
            Interlocked.Increment(ref _calls);
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
