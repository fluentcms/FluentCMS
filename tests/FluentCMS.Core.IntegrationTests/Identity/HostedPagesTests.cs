using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Providers;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// The hosted pages, driven the way a browser drives them, against the real authorize and token endpoints.
public sealed partial class HostedPagesTests : IAsyncDisposable
{
    private const string Password = "Correct-Horse-Battery-9";
    private const string NewPassword = "A-New-Longer-Passphrase-7";
    private const string ThirdPartyClientId = "agent-tool";
    private const string ThirdPartyRedirectUri = "https://agent.example/callback";
    private const string ThirdPartySignedOutUri = "https://agent.example/signed-out";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CapturedEmails _emails = new();
    private readonly FluentCmsTestHost _host;

    public HostedPagesTests()
    {
        _host = NewHost(_emails);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task AUserSignsInOnTheLoginPageAndTheAuthorizeRequestCompletesWithACode()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);

        using var challenge = await browser.GetAsync(request.Url);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = challenge.Headers.Location!;
        login.AbsolutePath.ShouldBe("/account/login");

        using var signedIn = await browser.SubmitAsync(login.PathAndQuery, Credentials(editor.Email, Password));
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();

        using var done = await browser.FollowAsync(signedIn);
        var tokens = await request.ExchangeAsync(_host, done);
        tokens.GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task FailedAndSuccessfulLoginsWriteActivityRows()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.7");

        var page = await browser.PageAsync("/account/login");
        using (var wrong = await browser.PostAsync("/account/login", page, Credentials(editor.Email, "not the password")))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await wrong.Content.ReadAsStringAsync(Token)).ShouldContain(LoginModel.Refused);
        }

        using (var unknown = await browser.PostAsync("/account/login", page, Credentials("nobody@example.com", Password)))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
        }

        using (var right = await browser.PostAsync("/account/login", page, Credentials(editor.Email, Password)))
        {
            right.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var rows = await ActivityAsync(_host, "user.login_", count: 3);

        var failed = rows.Single(row => row.Action == "user.login_failed" && row.TargetId is not null);
        failed.TargetType.ShouldBe("user");
        failed.TargetId.ShouldBe(editor.Id.ToString("D"));
        failed.Ip.ShouldBe("10.1.0.7");
        failed.ActorType.ShouldBe(PrincipalKind.Anonymous);
        failed.Origin.ShouldBe(Origin.Admin);
        Details(failed)["reason"].ShouldBe(LoginFailureReasons.InvalidCredentials);
        Details(failed)["method"].ShouldBe(LoginMethods.Password);

        var stranger = rows.Single(row => row.Action == "user.login_failed" && row.TargetId is null);
        stranger.TargetType.ShouldBeNull();
        stranger.TargetLabel.ShouldBeNull();
        Details(stranger)["reason"].ShouldBe(LoginFailureReasons.UnknownUser);
        stranger.Details!.ShouldNotContain("nobody@example.com");

        var succeeded = rows.Single(row => row.Action == "user.login_succeeded");
        succeeded.ActorType.ShouldBe(PrincipalKind.AdminUser);
        succeeded.ActorId.ShouldBe(editor.Id);
        succeeded.TargetId.ShouldBe(editor.Id.ToString("D"));
        succeeded.Ip.ShouldBe("10.1.0.7");
        succeeded.SiteId.ShouldBeNull();
        Details(succeeded)["method"].ShouldBe(LoginMethods.Password);
    }

    [Fact]
    public async Task ALockedOutUserGetsTheSameAnswerAsAWrongPassword()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        var page = await browser.PageAsync("/account/login");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await browser.PostAsync("/account/login", page, Credentials(editor.Email, "not the password"));
            wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var locked = await browser.PostAsync("/account/login", page, Credentials(editor.Email, Password));
        locked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await locked.Content.ReadAsStringAsync(Token)).ShouldContain("Sign-in failed.");

        var rows = await ActivityAsync(_host, "user.login_failed", count: 6);
        rows.Count(row => Details(row)["reason"] == LoginFailureReasons.LockedOut).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task AReturnUrlOfAnotherSiteIsNotFollowed()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);

        using var response = await browser.SubmitAsync("/account/login?ReturnUrl=https%3A%2F%2Fevil.example%2F", Credentials(editor.Email, Password));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task AnInvitedUserAcceptsTheInvitationSetsAPasswordAndCanSignIn()
    {
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var role = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Role>().AsNoTracking().SingleAsync(role => role.Name == BuiltInRoles.EditorName && role.SiteId == null, Token);
            await scope.ServiceProvider.GetRequiredService<InvitationService>().InviteAsync("ada@example.com", [role.Id], new Uri("https://localhost/account/accept-invitation"), Token);
        }

        var link = _emails.LinkTo("ada@example.com");
        link.AbsolutePath.ShouldBe("/account/accept-invitation");
        using var browser = new HostedPagesBrowser(_host);
        (await browser.PageAsync(link.PathAndQuery)).ShouldContain("ada@example.com");

        using var accepted = await browser.SubmitAsync(link.PathAndQuery, new Dictionary<string, string>
        {
            ["Input.UserName"] = "ada",
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        });
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        accepted.Headers.Location!.OriginalString.ShouldBe("/account/login?Status=invitation-accepted");
        (await browser.PageAsync(accepted.Headers.Location.OriginalString)).ShouldContain("Your account is ready.");

        (await browser.PageAsync(link.PathAndQuery)).ShouldContain("This invitation is not valid");

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var signedIn = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials("ada", Password));
        using var done = await browser.FollowAsync(signedIn);
        (await request.ExchangeAsync(_host, done)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ForgotAndResetPasswordChangeThePasswordWithASingleUseToken()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);

        using (var sent = await browser.SubmitAsync("/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = editor.Email }))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await sent.Content.ReadAsStringAsync(Token)).ShouldContain("is on its way");
        }

        var link = _emails.LinkTo(editor.Email);
        link.AbsolutePath.ShouldBe("/account/reset-password");
        QueryHelpers.ParseQuery(link.Query)["user"].ToString().ShouldBe(editor.Id.ToString("D"));

        using (var reset = await browser.SubmitAsync(link.PathAndQuery, NewPasswordFields(NewPassword)))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            reset.Headers.Location!.OriginalString.ShouldBe("/account/login?Status=password-reset");
        }

        using (var again = await browser.SubmitAsync(link.PathAndQuery, NewPasswordFields("Yet-Another-Passphrase-5")))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await again.Content.ReadAsStringAsync(Token)).ShouldContain("Invalid token.");
        }

        using (var old = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password)))
        {
            old.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var current = await browser.SubmitAsync("/account/login", Credentials(editor.Email, NewPassword));
        current.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task ForgotPasswordAnswersAnUnknownAddressTheSameAndSendsNothing()
    {
        using var browser = new HostedPagesBrowser(_host);

        using var sent = await browser.SubmitAsync("/account/forgot-password", new Dictionary<string, string> { ["Input.Email"] = "nobody@example.com" });

        sent.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await sent.Content.ReadAsStringAsync(Token)).ShouldContain("is on its way");
        _emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task AThirdPartyClientShowsTheConsentPageBeforeACodeIsIssued()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);

        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid profile management");
        using var asked = await browser.GetAsync(request.Url);
        asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var consent = asked.Headers.Location!.OriginalString;
        consent.ShouldStartWith("/account/consent?");

        var page = await browser.PageAsync(consent);
        page.ShouldContain("Agent Tool");
        page.ShouldContain(WebUtility.HtmlEncode(ConsentModel.Describe(TokenScopes.Management)));

        using var allowed = await browser.PostAsync(consent, page, new Dictionary<string, string> { ["decision"] = "allow" });
        allowed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        allowed.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
        using var done = await browser.FollowAsync(allowed, ThirdPartyRedirectUri);
        (await request.ExchangeAsync(_host, done)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();

        // The consent is kept, so the next request gets its code without the page.
        var next = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management");
        using var again = await browser.GetAsync(next.Url);
        again.Headers.Location!.AbsoluteUri.ShouldStartWith(ThirdPartyRedirectUri + "?code=");

        // A first-party client never asks.
        var admin = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var firstParty = await browser.GetAsync(admin.Url);
        firstParty.Headers.Location!.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
    }

    [Fact]
    public async Task DenyingTheConsentReturnsAccessDeniedAndPromptNoneReturnsConsentRequired()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);

        var silent = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management", prompt: "none");
        using (var none = await browser.GetAsync(silent.Url))
        {
            QueryHelpers.ParseQuery(none.Headers.Location!.Query)["error"].ToString().ShouldBe("consent_required");
        }

        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management");
        using var asked = await browser.GetAsync(request.Url);
        var consent = asked.Headers.Location!.OriginalString;
        using var denied = await browser.SubmitAsync(consent, new Dictionary<string, string> { ["decision"] = "deny" });
        using var back = await browser.FollowAsync(denied, ThirdPartyRedirectUri);

        back.Headers.Location!.AbsoluteUri.ShouldStartWith(ThirdPartyRedirectUri);
        QueryHelpers.ParseQuery(back.Headers.Location.Query)["error"].ToString().ShouldBe("access_denied");
    }

    [Fact]
    public async Task ConsentPostedWithoutTheTokenOfThePageIsRefused()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);
        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management");
        using var asked = await browser.GetAsync(request.Url);

        using var forged = await browser.PostAsync(asked.Headers.Location!.OriginalString, new Dictionary<string, string> { ["decision"] = "allow" });

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var still = await browser.GetAsync(request.Url);
        still.Headers.Location!.OriginalString.ShouldStartWith("/account/consent?");
    }

    [Fact]
    public async Task AUserWhoMustChangeThePasswordChoosesANewOneBeforeTheCode()
    {
        var editor = await CreateUserAsync(_host);
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.RequirePasswordChangeAsync((await users.FindByIdAsync(editor.Id.ToString()))!)).Succeeded.ShouldBeTrue();
        }

        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);

        using var sent = await browser.GetAsync(request.Url);
        sent.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var change = sent.Headers.Location!.OriginalString;
        change.ShouldStartWith("/account/change-password?ReturnUrl=");
        (await browser.PageAsync(change)).ShouldContain("You must choose a new password");

        using var changed = await browser.SubmitAsync(change, new Dictionary<string, string>
        {
            ["Input.CurrentPassword"] = Password,
            ["Input.Password"] = NewPassword,
            ["Input.ConfirmPassword"] = NewPassword,
        });
        changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var done = await browser.FollowAsync(changed);
        (await request.ExchangeAsync(_host, done)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ATwoFactorUserSignsInWithTheAuthenticatorCodeOrARecoveryCode()
    {
        var editor = await CreateUserAsync(_host);
        string key;
        string[] recovery;
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            var user = (await users.FindByIdAsync(editor.Id.ToString()))!;
            (await users.ResetAuthenticatorKeyAsync(user)).Succeeded.ShouldBeTrue();
            key = (await users.GetAuthenticatorKeyAsync(user))!;
            (await users.SetTwoFactorEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
            recovery = [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!];
        }

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using (var browser = new HostedPagesBrowser(_host, "10.1.0.2"))
        {
            using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials(editor.Email, Password));
            password.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            var twoFactor = password.Headers.Location!.OriginalString;
            twoFactor.ShouldStartWith("/account/two-factor?");
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();

            var page = await browser.PageAsync(twoFactor);
            using (var wrong = await browser.PostAsync(twoFactor, page, new Dictionary<string, string> { ["Input.Code"] = "000000" == Totp(key) ? "111111" : "000000" }))
            {
                wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using var right = await browser.PostAsync(twoFactor, page, new Dictionary<string, string> { ["Input.Code"] = Totp(key) });
            right.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            using var done = await browser.FollowAsync(right);
            (await request.ExchangeAsync(_host, done)).GetProperty("access_token").GetString().ShouldNotBeNullOrEmpty();
        }

        using (var browser = new HostedPagesBrowser(_host, "10.1.0.3"))
        {
            using var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
            var returnUrl = QueryHelpers.ParseQuery(new Uri(OAuthBase, password.Headers.Location!).Query)["ReturnUrl"].ToString();
            var recoveryPage = "/account/recovery-code?ReturnUrl=" + Uri.EscapeDataString(returnUrl);

            using var recovered = await browser.SubmitAsync(recoveryPage, new Dictionary<string, string> { ["Input.Code"] = recovery[0] });
            recovered.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
        }

        var rows = await ActivityAsync(_host, "user.login_", count: 3);
        rows.Where(row => row.Action == "user.login_succeeded").Select(row => Details(row)["method"]).ShouldBe([LoginMethods.TwoFactor, LoginMethods.RecoveryCode], ignoreOrder: true);
        rows.Single(row => row.Action == "user.login_failed").Ip.ShouldBe("10.1.0.2");
    }

    [Fact]
    public async Task TheSecondStepWithoutTheFirstGoesBackToTheLoginPage()
    {
        using var browser = new HostedPagesBrowser(_host);

        using var twoFactor = await browser.GetAsync("/account/two-factor");
        using var recovery = await browser.GetAsync("/account/recovery-code");

        twoFactor.Headers.Location!.OriginalString.ShouldStartWith("/account/login?ReturnUrl=");
        recovery.Headers.Location!.OriginalString.ShouldStartWith("/account/login?ReturnUrl=");
    }

    [Fact]
    public async Task LogoutAsksAndPostsAndAClientIsSentBack()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host);
        await SignInAsync(browser, editor.Email);

        var page = await browser.PageAsync("/account/logout");
        page.ShouldContain("Sign out?");
        page.ShouldContain("action=\"/connect/logout\"");
        page.ShouldNotContain("__RequestVerificationToken");

        using (var posted = await browser.PostAsync("/connect/logout", new Dictionary<string, string>()))
        {
            posted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            posted.Headers.Location!.OriginalString.ShouldBe("/account/logout");
        }

        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
        (await browser.PageAsync("/account/logout")).ShouldContain("You are signed out");

        // A client's logout without the id_token_hint comes to the page, which posts it back.
        await SignInAsync(browser, editor.Email);
        using var asked = await browser.GetAsync($"/connect/logout?client_id={ThirdPartyClientId}&post_logout_redirect_uri={Uri.EscapeDataString(ThirdPartySignedOutUri)}&state=s1");
        asked.Headers.Location!.OriginalString.ShouldStartWith("/account/logout?client_id=");
        var form = await browser.PageAsync(asked.Headers.Location.OriginalString);
        var fields = HiddenFields().Matches(form).ToDictionary(match => match.Groups[1].Value, match => WebUtility.HtmlDecode(match.Groups[2].Value));
        fields["post_logout_redirect_uri"].ShouldBe(ThirdPartySignedOutUri);

        using var back = await browser.PostAsync("/connect/logout", fields);
        back.Headers.Location!.AbsoluteUri.ShouldBe(ThirdPartySignedOutUri + "?state=s1");
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
    }

    [Fact]
    public async Task AnErrorTheClientCannotBeToldOfIsShownOnTheErrorPage()
    {
        using var browser = new HostedPagesBrowser(_host);

        using var refused = await browser.GetAsync($"/connect/authorize?client_id={FluentCmsTestHost.OAuthClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString("https://evil.example/callback")}&code_challenge=abcdefghijabcdefghijabcdefghijabcdefghijabc&code_challenge_method=S256");
        refused.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        refused.Headers.Location!.OriginalString.ShouldStartWith("/account/error?");

        using var page = await browser.GetAsync(refused.Headers.Location.OriginalString);
        page.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var html = await page.Content.ReadAsStringAsync(Token);
        html.ShouldContain("invalid_request");
        html.ShouldContain("asked for something this server does not allow");
    }

    [Fact]
    public async Task TheErrorPageShowsNoTextOfItsQuery()
    {
        using var browser = new HostedPagesBrowser(_host);

        using var response = await browser.GetAsync("/account/error?error=Call+us+now&error_description=Your+account+is+suspended");
        var html = await response.Content.ReadAsStringAsync(Token);

        html.ShouldNotContain("suspended");
        html.ShouldNotContain("Call us");
        html.ShouldContain("Something went wrong.");
    }

    [Fact]
    public async Task BrandingSettingsChangeTheLogoColoursAndNameOnThePages()
    {
        using var browser = new HostedPagesBrowser(_host);
        var standard = await browser.PageAsync("/account/login");
        standard.ShouldContain("<p>FluentCMS</p>");
        standard.ShouldContain($"--fcms-primary: {BrandingSettings.DefaultPrimaryColor};");

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            using var operation = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<ISettings<BrandingSettings>>().SetDefaultAsync(
                new BrandingSettings { Name = "Acme Publishing", LogoUrl = "https://cdn.acme.example/logo.svg", PrimaryColor = "#ff5500", BackgroundColor = "#000", TextColor = "#ffffff" },
                Token);
        }

        foreach (var path in new[] { "/account/login", "/account/forgot-password", "/account/error" })
        {
            using var response = await browser.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync(Token);
            html.ShouldContain("<img src=\"https://cdn.acme.example/logo.svg\" alt=\"Acme Publishing\">");
            html.ShouldContain("--fcms-primary: #ff5500; --fcms-background: #000; --fcms-text: #ffffff;");
            html.ShouldContain("- Acme Publishing</title>");
        }
    }

    [Fact]
    public async Task BrandingThatCouldBreakOutOfTheStyleSheetIsRefused()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var operation = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Cli, new SiteId(DefaultSite.Id));
        var settings = scope.ServiceProvider.GetRequiredService<ISettings<BrandingSettings>>();

        await Should.ThrowAsync<ValidationException>(async () => await settings.SetDefaultAsync(new BrandingSettings { PrimaryColor = "red; } body { display: none" }, Token));
        await Should.ThrowAsync<ValidationException>(async () => await settings.SetDefaultAsync(new BrandingSettings { LogoUrl = "javascript:alert(1)" }, Token));
        await Should.ThrowAsync<ValidationException>(async () => await settings.SetDefaultAsync(new BrandingSettings { LogoUrl = "//evil.example/logo.png" }, Token));
    }

    private static readonly Uri OAuthBase = HostedPagesBrowser.OAuthBase;

    private static FluentCmsTestHost NewHost(CapturedEmails emails) => new FluentCmsTestHost()
        .WithServices(services => services.AddSingleton<IEmailSender>(emails))
        .Configure(cms => cms.Clients.Applications[ThirdPartyClientId] = new ClientRegistration
        {
            DisplayName = "Agent Tool",
            RedirectUris = { ThirdPartyRedirectUri },
            PostLogoutRedirectUris = { ThirdPartySignedOutUri },
            Scopes = { "openid", "profile", "management" },
            RequireConsent = true,
        });

    private static async Task<TestUser> CreateUserAsync(FluentCmsTestHost host)
    {
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        (await users.AddPasswordAsync((await users.FindByIdAsync(editor.Id.ToString()))!, Password)).Succeeded.ShouldBeTrue();
        return editor;
    }

    private static async Task SignInAsync(HostedPagesBrowser browser, string userName)
    {
        using var response = await browser.SubmitAsync("/account/login", Credentials(userName, Password));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static Dictionary<string, string> Credentials(string userName, string password) => new()
    {
        ["Input.UserName"] = userName,
        ["Input.Password"] = password,
    };

    private static Dictionary<string, string> NewPasswordFields(string password) => new()
    {
        ["Input.Password"] = password,
        ["Input.ConfirmPassword"] = password,
    };

    private static Dictionary<string, string?> Details(ActivityRow row) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(row.Details ?? "{}")!;

    private static async Task<List<ActivityRow>> ActivityAsync(FluentCmsTestHost host, string actionPrefix, int count)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            await using var scope = host.Services.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking()
                .Where(row => row.Action.StartsWith(actionPrefix))
                .ToListAsync(Token);
            if (rows.Count >= count)
            {
                return rows;
            }

            await Task.Delay(100, Token);
        }

        throw new ShouldAssertException($"Fewer than {count} activity rows of {actionPrefix} were written.");
    }

    // RFC 6238 with Identity's parameters: SHA-1, 30 second steps, six digits.
    private static string Totp(string base32Key)
    {
        var key = Base32(base32Key);
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350 // TOTP is defined on HMAC-SHA1.
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var value = (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7fffffff) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = [];
        int buffer = 0, bits = 0;
        foreach (var character in text.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }

    [GeneratedRegex("<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\">")]
    private static partial Regex HiddenFields();

    // An authorization code request with PKCE, and the exchange of the code it brings back.
    private sealed record AuthorizeRequest(string Url, string ClientId, string RedirectUri, string Verifier)
    {
        public static AuthorizeRequest For(string clientId, string redirectUri, string scope = "openid offline_access management", string? prompt = null)
        {
            var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var query = QueryString.Create(new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["response_type"] = "code",
                ["redirect_uri"] = redirectUri,
                ["scope"] = scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "test",
                ["prompt"] = prompt,
            });
            return new AuthorizeRequest("/connect/authorize" + query, clientId, redirectUri, verifier);
        }

        public async Task<JsonElement> ExchangeAsync(FluentCmsTestHost host, HttpResponseMessage redirect)
        {
            redirect.StatusCode.ShouldBe(HttpStatusCode.Redirect, await redirect.Content.ReadAsStringAsync(Token));
            var location = redirect.Headers.Location!;
            location.AbsoluteUri.ShouldStartWith(RedirectUri);
            var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString();
            code.ShouldNotBeNullOrEmpty();

            using var client = host.CreateSecureClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = Verifier,
            });
            using var response = await client.PostAsync("/connect/token", content, Token);
            var body = await response.Content.ReadAsStringAsync(Token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }
    }

    private sealed class CapturedEmails : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent)
            {
                Sent.Add(message);
            }

            return Task.CompletedTask;
        }

        public Uri LinkTo(string address)
        {
            EmailMessage message;
            lock (Sent)
            {
                message = Sent.Last(sent => sent.To == address);
            }

            return new Uri(Regex.Match(message.TextBody, @"https://\S+").Value);
        }
    }
}
