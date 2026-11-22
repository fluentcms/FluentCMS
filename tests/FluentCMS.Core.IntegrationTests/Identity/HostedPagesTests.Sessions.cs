using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What ends or refuses a sign-in on the hosted pages (#2854).
public sealed partial class HostedPagesTests
{
    private const string SignInCookie = ".AspNetCore.Identity.Application";

    [Fact]
    public async Task TheRemoteSignOutOfAProviderEndsOnlyTheSignInWhoseSidAndIssItCarries()
    {
        var (idp, host) = (_idp, _sso);
        host.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(Corp).SignOutScheme.ShouldBe(IdentityConstants.ApplicationScheme);

        var rae = FakeIdentity.Of(UniqueAddress("rae")) with { Sid = "idp-session-" + Guid.NewGuid().ToString("N") };
        using var sso = await ProviderSignInAsync(host, idp, rae, Corp);

        // The same provider session through the other provider, which is trusted for its mfa, and a token without sid.
        var tess = FakeIdentity.Of(UniqueAddress("tess")) with { Sid = rae.Sid, Amr = ["pwd", "mfa"] };
        using var other = await ProviderSignInAsync(host, idp, tess, Trusted);
        using var bare = await ProviderSignInAsync(host, idp, FakeIdentity.Of(UniqueAddress("nils")), Corp);

        var editor = await CreateUserAsync(host);
        using var password = new HostedPagesBrowser(host);
        await SignInAsync(password, editor.Email);

        var ssoTokens = await CodeFlowAsync(host, sso);
        var others = new[] { other, bare, password };
        List<JsonElement> otherTokens = [];
        foreach (var browser in others)
        {
            otherTokens.Add(await CodeFlowAsync(host, browser));
        }

        var iss = Uri.EscapeDataString(idp.Issuer);
        var genuine = $"/account/sso/{Corp}/signout?sid={rae.Sid}&iss={iss}";

        // What any site can send: the path without the provider's session, or with values other than the sign-in's.
        foreach (var forged in new[]
        {
            $"/account/sso/{Corp}/signout",
            $"/account/sso/{Corp}/signout?sid={rae.Sid}",
            $"/account/sso/{Corp}/signout?iss={iss}",
            $"/account/sso/{Corp}/signout?sid=&iss={iss}",
            $"/account/sso/{Corp}/signout?sid=made-up&iss={iss}",
            $"/account/sso/{Corp}/signout?sid={rae.Sid}&iss=https%3A%2F%2Fevil.example",
            $"/account/sso/{Corp}/signout?sid={rae.Sid!.ToUpperInvariant()}&iss={iss}",
            $"/account/sso/{Corp}/signout?sid={rae.Sid}&iss={Uri.EscapeDataString(idp.Issuer + "/")}",
            $"/account/sso/{Trusted}/signout?sid={rae.Sid}&iss={iss}",
            $"/account/sso/{Corp}/signout?sid={rae.Sid}&sid=made-up&iss={iss}",
            $"/account/sso/{Corp}/signout?sid=made-up&sid={rae.Sid}&iss={iss}",
            $"/account/sso/{Corp}/signout?sid={rae.Sid}&iss={iss}&iss=https%3A%2F%2Fevil.example",
            $"/account/sso/{Corp}/signout?sid={rae.Sid}&iss=https%3A%2F%2Fevil.example&iss={iss}",
        })
        {
            using var response = await sso.GetAsync(forged);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, forged);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue(forged);
            sso.HasCookie(SignInCookie).ShouldBeTrue(forged);
        }

        using (var posted = await sso.PostAsync($"/account/sso/{Corp}/signout", new Dictionary<string, string> { ["sid"] = "made-up", ["iss"] = idp.Issuer }))
        {
            posted.StatusCode.ShouldBe(HttpStatusCode.OK);
            sso.HasCookie(SignInCookie).ShouldBeTrue();
        }

        // A form with a parameter twice, in either order.
        foreach (var body in new[]
        {
            $"sid={rae.Sid}&sid=made-up&iss={iss}",
            $"sid=made-up&sid={rae.Sid}&iss={iss}",
            $"sid={rae.Sid}&iss={iss}&iss=https%3A%2F%2Fevil.example",
            $"sid={rae.Sid}&iss=https%3A%2F%2Fevil.example&iss={iss}",
        })
        {
            using var form = new StringContent(body, System.Text.Encoding.ASCII, "application/x-www-form-urlencoded");
            using var posted = await sso.SendAsync(HttpMethod.Post, $"/account/sso/{Corp}/signout", form);
            posted.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            sso.HasCookie(SignInCookie).ShouldBeTrue(body);
        }

        var refreshToken = await RotatedAsync(host, Text(ssoTokens, "refresh_token"));
        (await MeAsync(host, Text(ssoTokens, "access_token"))).ShouldBe(HttpStatusCode.OK);

        // The genuine request leaves a sign-in made another way as it is: a password, the other provider, a token without sid.
        foreach (var browser in others)
        {
            using var response = await browser.GetAsync(genuine);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(SignInCookie).ShouldBeTrue();
        }

        // It ends the sign-in it names with its server session, whose tokens are refused, as after a confirmed logout.
        using (var signedOut = await sso.GetAsync(genuine))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
            signedOut.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }

        sso.HasCookie(SignInCookie).ShouldBeFalse();
        (await RefusedAsync(host, refreshToken)).ShouldBe("invalid_grant");
        (await MeAsync(host, Text(ssoTokens, "access_token"))).ShouldBe(HttpStatusCode.Unauthorized);
        foreach (var tokens in otherTokens)
        {
            (await RotatedAsync(host, Text(tokens, "refresh_token"))).ShouldNotBeNullOrEmpty();
        }

        // The other provider's own request ends its own sign-in, which the trusted sign-in made with amr=mfa.
        using (var signedOut = await other.GetAsync($"/account/sso/{Trusted}/signout?sid={tess.Sid}&iss={iss}"))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        other.HasCookie(SignInCookie).ShouldBeFalse();
    }

    // A host that sets its own events on a provider, as Events or as EventsType, keeps the check of the remote sign-out and
    // the sid and iss that the sign-in keeps; the host's events run after FluentCMS's (#3225). The end is an activity row of
    // the system (#3207). The test host registers a host's services before AddFluentCms, where a Configure of the events
    // would run before FluentCMS's, so they are set in a PostConfigure, which runs after every Configure as a host's
    // Configure after AddFluentCms does (#3531).
    [Fact]
    public async Task AHostsOwnEventsKeepTheCheckOfTheRemoteSignOut()
    {
        using var idp = new FakeOidcProvider();
        foreach (var asType in new[] { false, true })
        {
            var events = new HostEvents();
            await using var host = SsoHost(idp).WithServices(services =>
            {
                services.AddSingleton(events);
                services.PostConfigure<OpenIdConnectOptions>(Corp, options =>
                {
                    if (asType)
                    {
                        options.EventsType = typeof(HostEvents);
                    }
                    else
                    {
                        options.Events = events;
                    }
                });
            });

            var ann = FakeIdentity.Of(UniqueAddress("ann")) with { Sid = "idp-session-" + Guid.NewGuid().ToString("N") };
            using var browser = await ProviderSignInAsync(host, idp, ann, Corp);
            events.TokensValidated.ShouldBe(1);
            var iss = Uri.EscapeDataString(idp.Issuer);

            foreach (var forged in new[] { $"/account/sso/{Corp}/signout", $"/account/sso/{Corp}/signout?sid=made-up&iss={iss}" })
            {
                using var response = await browser.GetAsync(forged);
                response.StatusCode.ShouldBe(HttpStatusCode.OK, forged);
                browser.HasCookie(SignInCookie).ShouldBeTrue(forged);
            }

            using (var signedOut = await browser.GetAsync($"/account/sso/{Corp}/signout?sid={ann.Sid}&iss={iss}"))
            {
                signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            browser.HasCookie(SignInCookie).ShouldBeFalse();
            events.RemoteSignOuts.ShouldBe(3);

            var user = await LinkedUserAsync(host, Corp, ann.Subject);
            var ended = (await ActivityAsync(host, "user.sessions_ended", count: 1, of: row => row.TargetId == user.Id.ToString("D"))).ShouldHaveSingleItem();
            ended.ActorType.ShouldBe(PrincipalKind.System);
            Details(ended)["reason"].ShouldBe(SessionEndReasons.RemoteSignOut);
            ended.Details!.ShouldNotContain(ann.Sid!);
        }
    }

    // A sign-out whose activity row cannot be written still deletes the cookie of the sign-in whose tokens it revoked, and
    // answers as it otherwise does; the missing row is logged, with no token (#3531). The confirmed logout and a provider's
    // remote sign-out.
    [Fact]
    public async Task ASignOutWhoseRowCannotBeWrittenStillDeletesItsCookie()
    {
        using var idp = new FakeOidcProvider();
        var failure = new SessionRowFailure();
        var logs = new LogCapture();
        await using var host = SsoHost(idp)
            .WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)))
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(failure)));

        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);
        await SignInAsync(browser, editor.Email);
        var tokens = await CodeFlowAsync(host, browser);
        await SignOutAsync(browser);
        browser.HasCookie(SignInCookie).ShouldBeFalse();
        (await RefusedAsync(host, Text(tokens, "refresh_token"))).ShouldBe("invalid_grant");

        var ann = FakeIdentity.Of(UniqueAddress("ann")) with { Sid = "idp-session-" + Guid.NewGuid().ToString("N") };
        using var sso = await ProviderSignInAsync(host, idp, ann, Corp);
        var ssoTokens = await CodeFlowAsync(host, sso);
        using (var signedOut = await sso.GetAsync($"/account/sso/{Corp}/signout?sid={ann.Sid}&iss={Uri.EscapeDataString(idp.Issuer)}"))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        sso.HasCookie(SignInCookie).ShouldBeFalse();
        (await RefusedAsync(host, Text(ssoTokens, "refresh_token"))).ShouldBe("invalid_grant");

        failure.Failed.ShouldBe(2);
        (await ActivityAsync(host, "user.sessions_ended", count: 0)).ShouldBeEmpty();
        var user = await LinkedUserAsync(host, Corp, ann.Subject);
        var missing = logs.Entries.Where(entry => entry.EventId.Id == AccountLog.SessionEndNotRecordedId).ToList();
        missing.Select(entry => (entry.Level, entry.State["Pool"] as string, entry.State["Id"] as Guid?, entry.State["Reason"] as string)).ShouldBe(
            [(LogLevel.Error, "user", editor.Id, SessionEndReasons.SignedOut), (LogLevel.Error, "user", user.Id, SessionEndReasons.RemoteSignOut)]);
        missing.ShouldAllBe(entry => !entry.Message.Contains(Text(tokens, "refresh_token")) && !entry.Message.Contains(ann.Sid!));
    }

    private sealed class HostEvents : OpenIdConnectEvents
    {
        private int _tokensValidated;
        private int _remoteSignOuts;

        public int TokensValidated => Volatile.Read(ref _tokensValidated);

        public int RemoteSignOuts => Volatile.Read(ref _remoteSignOuts);

        public override Task TokenValidated(TokenValidatedContext context)
        {
            Interlocked.Increment(ref _tokensValidated);
            return base.TokenValidated(context);
        }

        public override Task RemoteSignOut(RemoteSignOutContext context)
        {
            Interlocked.Increment(ref _remoteSignOuts);
            return base.RemoteSignOut(context);
        }
    }

    [Fact]
    public async Task TheRemoteSignOutEndsAProviderSignInThatGaveTheLocalSecondFactorAfterIt()
    {
        var (idp, host) = (_idp, _sso);
        var kim = FakeIdentity.Of(UniqueAddress("kim")) with { Sid = "idp-session-" + Guid.NewGuid().ToString("N") };
        var key = await ProviderUserWithTwoFactorAsync(host, idp, kim);

        using var browser = new HostedPagesBrowser(host);
        using var back = await SignInWithProviderAsync(browser, idp, kim);
        var challenge = await ExternalLoginAsync(browser, back);
        challenge.ShouldStartWith("/account/two-factor");
        using (var passed = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = Totp(host, key) }))
        {
            passed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var tokens = await CodeFlowAsync(host, browser);
        var iss = Uri.EscapeDataString(idp.Issuer);
        using (var forged = await browser.GetAsync($"/account/sso/{Corp}/signout?sid=made-up&iss={iss}"))
        {
            forged.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        browser.HasCookie(SignInCookie).ShouldBeTrue();
        var refreshToken = await RotatedAsync(host, Text(tokens, "refresh_token"));

        using (var signedOut = await browser.GetAsync($"/account/sso/{Corp}/signout?sid={kim.Sid}&iss={iss}"))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        browser.HasCookie(SignInCookie).ShouldBeFalse();
        (await RefusedAsync(host, refreshToken)).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task ASecondFactorKeepsNoProviderSessionFromAnotherSignInStartedInTheSameBrowser()
    {
        var (idp, host) = (_idp, _sso);
        var kim = FakeIdentity.Of(UniqueAddress("kim")) with { Sid = "idp-session-" + Guid.NewGuid().ToString("N") };
        var key = await ProviderUserWithTwoFactorAsync(host, idp, kim);

        using var browser = new HostedPagesBrowser(host);
        using var back = await SignInWithProviderAsync(browser, idp, kim);
        var challenge = await ExternalLoginAsync(browser, back);
        challenge.ShouldStartWith("/account/two-factor");

        // Another sign-in at the provider reaches the callback in the same browser before kim gives the code.
        var zed = FakeIdentity.Of(UniqueAddress("zed")) with { Sid = "idp-session-" + Guid.NewGuid().ToString("N") };
        using (await SignInWithProviderAsync(browser, idp, zed))
        {
        }

        using (var passed = await browser.SubmitAsync(challenge, new Dictionary<string, string> { ["Input.Code"] = Totp(host, key) }))
        {
            passed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        browser.HasCookie(SignInCookie).ShouldBeTrue();
        var tokens = await CodeFlowAsync(host, browser);
        (await MeAsync(host, Text(tokens, "access_token"))).ShouldBe(HttpStatusCode.OK);

        // The sign-in keeps no session of the provider, so neither zed's nor kim's sign-out names it.
        var iss = Uri.EscapeDataString(idp.Issuer);
        foreach (var sid in new[] { zed.Sid, kim.Sid })
        {
            using var response = await browser.GetAsync($"/account/sso/{Corp}/signout?sid={sid}&iss={iss}");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(SignInCookie).ShouldBeTrue();
        }

        (await RotatedAsync(host, Text(tokens, "refresh_token"))).ShouldNotBeNullOrEmpty();
    }

    // A user made by a first sign-in through the provider, with the local authenticator turned on. Returns its key.
    private static async Task<string> ProviderUserWithTwoFactorAsync(FluentCmsTestHost host, FakeOidcProvider idp, FakeIdentity user)
    {
        using (await ProviderSignInAsync(host, idp, user, Corp))
        {
        }

        await using var scope = host.Services.CreateAsyncScope();
        var stored = (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByLoginAsync(Corp, user.Subject)).ShouldNotBeNull();
        var twoFactor = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
        var key = (await twoFactor.EnrolAsync(stored, "FluentCMS")).SharedKey;
        await twoFactor.VerifyAsync(stored, Totp(host, key));
        return key;
    }

    // An address no other test of the shared hosts uses.
    private static string UniqueAddress(string name) => $"{name}-{Guid.NewGuid():N}@corp.example";

    // A browser signed in on the hosted pages through the provider.
    private static async Task<HostedPagesBrowser> ProviderSignInAsync(FluentCmsTestHost host, FakeOidcProvider idp, FakeIdentity user, string provider)
    {
        var browser = new HostedPagesBrowser(host);
        using var back = await SignInWithProviderAsync(browser, idp, user, provider: provider);
        (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        return browser;
    }

    [Fact]
    public async Task ATokenThatDoesNotSayTheAddressIsVerifiedCreatesNoUserUnlessTheProviderAllowsIt()
    {
        new SsoProvider().RequireVerifiedEmail.ShouldBeTrue();
        using var idp = new FakeOidcProvider();
        var quiet = FakeIdentity.Of("quiet@corp.example") with { EmailVerified = null };

        using (var browser = new HostedPagesBrowser(_sso))
        {
            using var back = await SignInWithProviderAsync(browser, _idp, quiet);
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
            await using var scope = _sso.Services.CreateAsyncScope();
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
        var host = _host;
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

        var failures = await ActivityAsync(host, "user.login_failed", count: 6, of: row => row.TargetId == editor.Id.ToString("D"));
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
        var host = _host;
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
        browser.HasCookie(SignInCookie).ShouldBeTrue();

        // Signing in again over the cookie answers max_age.
        using var signedIn = await browser.SubmitAsync(login.PathAndQuery, Credentials(editor.Email, Password));
        signedIn.Headers.Location!.OriginalString.ShouldContain("max_age");
        using var done = await browser.FollowAsync(signedIn);
        (await request.ExchangeAsync(host, done)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task TheHostedPagesSendASignInPastTheAbsoluteLifetimeToTheLoginPageAndKeepTheCookie()
    {
        await using var host = NewHost(_emails).Configure(cms => cms.Clients.AbsoluteSessionLifetime = TimeSpan.FromHours(8));
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);
        await SignInAsync(browser, editor.Email);
        var consent = HostedPagePaths.Consent + new Uri(OAuthBase, AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid profile management").Url).Query;
        string[] pages = [HostedPagePaths.ChangePassword, HostedPagePaths.EnableTwoFactor, consent];
        foreach (var path in pages)
        {
            (await PathAsync(browser, path)).ShouldBe(new Uri(OAuthBase, path).AbsolutePath, path);
        }

        // Past the lifetime the pages ask for a new sign-in, however often the sliding cookie was renewed; the logout page
        // still answers.
        host.Time.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        foreach (var path in pages)
        {
            (await PathAsync(browser, path)).ShouldBe(HostedPagePaths.Login, path);
            browser.HasCookie(SignInCookie).ShouldBeTrue(path);
        }

        (await PathAsync(browser, HostedPagePaths.Logout)).ShouldBe(HostedPagePaths.Logout);

        await SignInAsync(browser, editor.Email);
        (await PathAsync(browser, HostedPagePaths.ChangePassword)).ShouldBe(HostedPagePaths.ChangePassword);
    }

    // The path a GET of a hosted page ends on: its own when it answers, the login page's when it sends the user there.
    private static async Task<string> PathAsync(HostedPagesBrowser browser, string path)
    {
        using var response = await browser.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            return new Uri(OAuthBase, response.Headers.Location!).AbsolutePath;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return new Uri(OAuthBase, path).AbsolutePath;
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
