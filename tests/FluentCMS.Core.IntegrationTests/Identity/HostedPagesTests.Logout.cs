using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// What a logout ends of the sessions of its user (#3198).
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task AConfirmedLogoutRevokesTheTokensOfItsSessionAndLeavesTheUsersOtherSessions()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.16");
        using var otherDevice = new HostedPagesBrowser(_host, "10.1.0.9");
        await SignInAsync(browser, editor.Email);
        await SignInAsync(otherDevice, editor.Email);
        var tokens = await CodeFlowAsync(_host, browser);
        var secondTab = await CodeFlowAsync(_host, browser);
        var other = await CodeFlowAsync(_host, otherDevice);

        // A logout that is not confirmed, by a GET without a hint or a POST without the page's token, revokes nothing.
        using (var asked = await browser.GetAsync("/connect/logout"))
        {
            asked.Headers.Location!.OriginalString.ShouldBe("/account/logout");
        }

        using (var forged = await browser.PostAsync("/connect/logout", new Dictionary<string, string>()))
        {
            forged.Headers.Location!.OriginalString.ShouldBe("/account/logout");
        }

        var refreshToken = await RotatedAsync(_host, Text(tokens, "refresh_token"));
        (await MeAsync(_host, Text(tokens, "access_token"))).ShouldBe(HttpStatusCode.OK);

        await SignOutAsync(browser);

        (await RefusedAsync(_host, refreshToken)).ShouldBe("invalid_grant");
        (await RefusedAsync(_host, Text(secondTab, "refresh_token"))).ShouldBe("invalid_grant");
        (await MeAsync(_host, Text(tokens, "access_token"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await RotatedAsync(_host, Text(other, "refresh_token"))).ShouldNotBeNullOrEmpty();
        (await MeAsync(_host, Text(other, "access_token"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ALogoutWithAHintRevokesOnlyWhenItEndsTheSessionOfItsCookie()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.17");
        using var otherDevice = new HostedPagesBrowser(_host, "10.1.0.11");
        await SignInAsync(browser, editor.Email);
        await SignInAsync(otherDevice, editor.Email);
        var tokens = await CodeFlowAsync(_host, browser);
        var hint = "/connect/logout?id_token_hint=" + Uri.EscapeDataString(Text(tokens, "id_token"));

        // Without the cookie there is no session to end, and the hint alone revokes nothing.
        using (var stranger = new HostedPagesBrowser(_host, "10.1.0.10"))
        using (var returned = await stranger.GetAsync(hint))
        {
            returned.Headers.Location!.OriginalString.ShouldBe("/account/logout");
        }

        // The user's hint is not the other device's session: that device is asked, and its cookie and this session stay.
        using (var elsewhere = await otherDevice.GetAsync(hint))
        {
            elsewhere.Headers.Location!.OriginalString.ShouldStartWith("/account/logout?");
        }

        otherDevice.HasCookie(SignInCookie).ShouldBeTrue();
        var refreshToken = await RotatedAsync(_host, Text(tokens, "refresh_token"));

        using (var signedOut = await browser.GetAsync(hint))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        browser.HasCookie(SignInCookie).ShouldBeFalse();
        (await RefusedAsync(_host, refreshToken)).ShouldBe("invalid_grant");

        // Logging out again with the same hint, as a second tab or a double click does, finds nothing to end and asks nothing.
        using var again = await browser.GetAsync(hint);
        again.Headers.Location!.OriginalString.ShouldBe("/account/logout");
    }

    [Fact]
    public async Task ALogoutLeavesTheConsentOfAThirdPartyClient()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.18");
        await SignInAsync(browser, editor.Email);

        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management");
        using var asked = await browser.GetAsync(request.Url);
        var consent = asked.Headers.Location!.OriginalString;
        var page = await browser.PageAsync(consent);
        using var allowed = await browser.PostAsync(consent, page, new Dictionary<string, string> { ["decision"] = "allow" });
        using var done = await browser.FollowAsync(allowed, ThirdPartyRedirectUri);
        var issued = await request.ExchangeAsync(_host, done);

        // A consent is no session's, so its identity token names none, and its hint is asked about.
        Payload(Text(issued, "id_token")).TryGetProperty("sid", out _).ShouldBeFalse();
        using (var hinted = await browser.GetAsync("/connect/logout?id_token_hint=" + Uri.EscapeDataString(Text(issued, "id_token"))))
        {
            hinted.Headers.Location!.OriginalString.ShouldStartWith("/account/logout?");
        }

        browser.HasCookie(SignInCookie).ShouldBeTrue();
        await SignOutAsync(browser);

        // The client keeps its token, and the next sign-in is not asked again.
        (await MeAsync(_host, Text(issued, "access_token"))).ShouldBe(HttpStatusCode.OK);
        await SignInAsync(browser, editor.Email);
        using var again = await browser.GetAsync(AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management").Url);
        again.Headers.Location!.AbsoluteUri.ShouldStartWith(ThirdPartyRedirectUri + "?code=");
    }

    [Fact]
    public async Task ASignInOverALiveOneKeepsTheSessionOfItsUserAndEndsAnothers()
    {
        var editor = await CreateUserAsync(_host);
        var other = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.19");
        await SignInAsync(browser, editor.Email);
        var before = await CodeFlowAsync(_host, browser);

        // The same user signs in again on the login page: the browser's session goes on, and its logout ends both.
        await SignInAsync(browser, editor.Email);
        var after = await CodeFlowAsync(_host, browser);
        Sid(after).ShouldBe(Sid(before));
        await SignOutAsync(browser);
        (await RefusedAsync(_host, Text(before, "refresh_token"))).ShouldBe("invalid_grant");
        (await RefusedAsync(_host, Text(after, "refresh_token"))).ShouldBe("invalid_grant");

        // Another user signs in over a live sign-in: the first user's session in this browser ends.
        await SignInAsync(browser, editor.Email);
        var replaced = await CodeFlowAsync(_host, browser);
        await SignInAsync(browser, other.Email);
        (await RefusedAsync(_host, Text(replaced, "refresh_token"))).ShouldBe("invalid_grant");
        Sid(await CodeFlowAsync(_host, browser)).ShouldNotBe(Sid(replaced));
    }

    [Fact]
    public async Task AReauthenticationAnotherSiteCanAskForEndsTheCookieAndRevokesNothing()
    {
        await using var host = NewHost(_emails).Configure(cms => cms.Clients.AbsoluteSessionLifetime = TimeSpan.FromHours(8));
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);

        // A top-level GET from another site carries the cookie and nothing secret: prompt=login, max_age=0, and any
        // authorize request past the absolute lifetime sign the cookie out and leave the session's tokens working.
        var authorize = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri).Url;
        var login = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri, prompt: "login").Url;
        foreach (var (url, wait) in new[] { (login, TimeSpan.Zero), (authorize + "&max_age=0", TimeSpan.Zero), (authorize, TimeSpan.FromHours(2)) })
        {
            await SignInAsync(browser, editor.Email);
            host.Time.Advance(TimeSpan.FromHours(7));
            var tokens = await CodeFlowAsync(host, browser);
            host.Time.Advance(wait);

            using var foreign = await browser.GetAsync(url);
            new Uri(OAuthBase, foreign.Headers.Location!).AbsolutePath.ShouldBe("/account/login", url);
            browser.HasCookie(SignInCookie).ShouldBeFalse();

            var renewed = await RenewedAsync(host, Text(tokens, "refresh_token"));
            (await MeAsync(host, Text(renewed, "access_token"))).ShouldBe(HttpStatusCode.OK, url);
        }
    }

    [Fact]
    public async Task ASecondFactorTheRoleNowRequiresEndsTheCookieAtAuthorizeAndRevokesNothing()
    {
        using var admin = await _host.AsUserAsync(await _host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(_host);
        var (key, _) = await EnableTwoFactorAsync(_host, editor);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.13");

        // A sign-in with the second factor on a remembered device, then one with the password alone over it, in one session.
        using (var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password)))
        using (var remembered = await browser.SubmitAsync(password.Headers.Location!.OriginalString, new Dictionary<string, string> { ["Input.Code"] = Totp(key), ["Input.RememberMachine"] = "true" }))
        {
            remembered.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var withSecondFactor = await CodeFlowAsync(_host, browser);
        await SignInAsync(browser, editor.Email);
        var withPassword = await CodeFlowAsync(_host, browser);
        Sid(withPassword).ShouldBe(Sid(withSecondFactor));

        // Another site sends the browser to a plain authorize request once the role requires the second factor: the cookie
        // ends, and the tokens are left to the token endpoint, which refuses only those without it.
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"users/{editor.Id:D}/roles/{await GuardedRoleAsync(admin)}")).Status.ShouldBe(HttpStatusCode.OK);
        using var challenged = await browser.GetAsync(AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri).Url);
        challenged.Headers.Location!.AbsolutePath.ShouldBe("/account/login");
        browser.HasCookie(SignInCookie).ShouldBeFalse();

        (await MeAsync(_host, Text(withSecondFactor, "access_token"))).ShouldBe(HttpStatusCode.OK);
        (await RotatedAsync(_host, Text(withSecondFactor, "refresh_token"))).ShouldNotBeNullOrEmpty();
        (await RefusedAsync(_host, Text(withPassword, "refresh_token"))).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task AHintWithoutTheCookieReturnsOnlyAnIdentityTokenOfThisServerToItsOwnClient()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.14");
        using var stranger = new HostedPagesBrowser(_host, "10.1.0.15");
        var signedOut = Uri.EscapeDataString(ThirdPartySignedOutUri);

        // Not an identity token of this server: something else, one signed with another key, and an access token of it.
        await SignInAsync(browser, editor.Email);
        var tokens = await CodeFlowAsync(_host, browser);
        using var otherKey = RSA.Create(2048);
        var foreign = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = OAuthBase.AbsoluteUri,
            Audience = ThirdPartyClientId,
            Claims = new Dictionary<string, object> { ["sub"] = editor.Id.ToString("D"), ["azp"] = ThirdPartyClientId },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(otherKey), SecurityAlgorithms.RsaSha256),
        });
        foreach (var hint in new[] { "not.a.token", foreign, Text(tokens, "access_token") })
        {
            using var response = await stranger.GetAsync($"/connect/logout?id_token_hint={Uri.EscapeDataString(hint)}&client_id={ThirdPartyClientId}&post_logout_redirect_uri={signedOut}");
            response.Headers.Location!.OriginalString.ShouldStartWith("/account/logout?");
        }

        // A revoked identity token of the test client is not the third-party client's, whose signed-out URI it asks for.
        await SignOutAsync(browser);
        var revoked = Uri.EscapeDataString(Text(tokens, "id_token"));
        foreach (var query in new[] { $"&post_logout_redirect_uri={signedOut}", $"&client_id={ThirdPartyClientId}&post_logout_redirect_uri={signedOut}", $"&client_id={ThirdPartyClientId}" })
        {
            using var response = await stranger.GetAsync($"/connect/logout?id_token_hint={revoked}{query}&state=s1");
            response.Headers.Location!.OriginalString.ShouldStartWith("/account/logout?", Case.Sensitive, query);
        }

        // The third-party client's own revoked hint returns to its own signed-out URI without asking.
        await SignInAsync(browser, editor.Email);
        var request = AuthorizeRequest.For(ThirdPartyClientId, ThirdPartyRedirectUri, "openid management");
        using var asked = await browser.GetAsync(request.Url);
        var consent = asked.Headers.Location!.OriginalString;
        using var allowed = await browser.PostAsync(consent, await browser.PageAsync(consent), new Dictionary<string, string> { ["decision"] = "allow" });
        using var done = await browser.FollowAsync(allowed, ThirdPartyRedirectUri);
        var own = Uri.EscapeDataString(Text(await request.ExchangeAsync(_host, done), "id_token"));
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UserSessions>().RevokeAllAsync(editor.Id, Token);
        }

        using var back = await stranger.GetAsync($"/connect/logout?id_token_hint={own}&client_id={ThirdPartyClientId}&post_logout_redirect_uri={signedOut}&state=s1");
        back.Headers.Location!.AbsoluteUri.ShouldBe(ThirdPartySignedOutUri + "?state=s1");
    }
    [Fact]
    public async Task ASignInWithTheSecondFactorAndAPasswordChangeKeepTheSessionThatTheLogoutEnds()
    {
        var editor = await CreateUserAsync(_host);
        var (key, _) = await EnableTwoFactorAsync(_host, editor);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.12");
        using (var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password)))
        using (var challenged = await browser.SubmitAsync(password.Headers.Location!.OriginalString, new Dictionary<string, string> { ["Input.Code"] = Totp(key) }))
        {
            challenged.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var first = await CodeFlowAsync(_host, browser);

        using (var changed = await browser.SubmitAsync("/account/change-password", new Dictionary<string, string>(NewPasswordFields("A-changed-password-1")) { ["Input.CurrentPassword"] = Password }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var second = await CodeFlowAsync(_host, browser);
        Sid(second).ShouldBe(Sid(first));

        await SignOutAsync(browser);
        (await RefusedAsync(_host, Text(second, "refresh_token"))).ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task TheLogoutOfACookieFromBeforeSessionsWereMarkedEndsTheCookieAlone()
    {
        var editor = await CreateUserAsync(_host);
        using var browser = new HostedPagesBrowser(_host, "10.1.0.20");
        browser.SetCookie(await UnmarkedCookieAsync(editor));
        var tokens = await CodeFlowAsync(_host, browser);
        Payload(Text(tokens, "id_token")).TryGetProperty("sid", out _).ShouldBeFalse();

        await SignOutAsync(browser);

        browser.HasCookie(SignInCookie).ShouldBeFalse();
        (await RotatedAsync(_host, Text(tokens, "refresh_token"))).ShouldNotBeNullOrEmpty();
    }

    // The code flow of the test client in the browser's session.
    private static async Task<JsonElement> CodeFlowAsync(FluentCmsTestHost host, HostedPagesBrowser browser)
    {
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var redirect = await browser.GetAsync(request.Url);
        return await request.ExchangeAsync(host, redirect);
    }

    // The rotated refresh token.
    private static async Task<string> RotatedAsync(FluentCmsTestHost host, string refreshToken)
    {
        using var response = await RefreshAsync(host, refreshToken);
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("refresh_token").GetString()!;
    }

    private static async Task<JsonElement> RenewedAsync(FluentCmsTestHost host, string refreshToken)
    {
        using var response = await RefreshAsync(host, refreshToken);
        var body = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.Clone();
    }

    private static async Task<string?> RefusedAsync(FluentCmsTestHost host, string refreshToken)
    {
        using var response = await RefreshAsync(host, refreshToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        return json.RootElement.GetProperty("error").GetString();
    }

    private static async Task<HttpStatusCode> MeAsync(FluentCmsTestHost host, string accessToken)
    {
        using var client = host.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.GetAsync("/api/management/v1/me", Token);
        return response.StatusCode;
    }

    // A sign-in cookie as one made before sessions were marked: the time of the sign-in and no server session.
    private async Task<string> UnmarkedCookieAsync(TestUser user)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var stored = await provider.GetRequiredService<CmsUserManager>().FindByIdAsync(user.Id.ToString());
        var principal = await provider.GetRequiredService<SignInManager<CmsUser>>().CreateUserPrincipalAsync(stored!);
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var now = _host.Time.GetUtcNow();
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + options.ExpireTimeSpan };
        properties.Items[CmsSignInManager.SignedInAtItem] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ticket = new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    private static string Text(JsonElement tokens, string name) => tokens.GetProperty(name).GetString()!;

    private static string? Sid(JsonElement tokens) => Payload(Text(tokens, "id_token")).GetProperty("sid").GetString();

    private static JsonElement Payload(string jwt)
    {
        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }
}
