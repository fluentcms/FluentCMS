using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Two-factor sign-in (#2780): enrolment through /me/two-factor and the hosted page, the challenge, recovery codes, the
// RequireTwoFactor flag of roles at the authorize and token endpoints, and amr in the tokens.
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task AUserEnrolsAnAuthenticatorThroughMeAndIsChallengedAtTheNextLogin()
    {
        await using var host = TwoFactorHost();
        var editor = await CreateUserAsync(host);
        var before = await host.SignInAsync(editor);
        using var client = await host.AsUserAsync(editor);

        var status = await AdminFixture.SendAsync(client, HttpMethod.Get, "me/two-factor");
        status.Status.ShouldBe(HttpStatusCode.OK, status.Body?.ToJsonString());
        status.Json["enabled"]!.GetValue<bool>().ShouldBeFalse();
        status.Json["required"]!.GetValue<bool>().ShouldBeFalse();
        status.Json["pending"]!.GetValue<bool>().ShouldBeFalse();

        var enrolled = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/enrol");
        enrolled.Status.ShouldBe(HttpStatusCode.OK, enrolled.Body?.ToJsonString());
        var key = enrolled.Json["sharedKey"]!.GetValue<string>();
        enrolled.Json["authenticatorUri"]!.GetValue<string>().ShouldBe($"otpauth://totp/FluentCMS:{Uri.EscapeDataString(editor.Email)}?secret={key}&issuer=FluentCMS&digits=6");
        (await AdminFixture.SendAsync(client, HttpMethod.Get, "me/two-factor")).Json["pending"]!.GetValue<bool>().ShouldBeTrue();

        var wrong = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/verify", Code(WrongCode(key)));
        wrong.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        wrong.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidTwoFactorCode);

        var verified = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/verify", Code(Totp(key)));
        verified.Status.ShouldBe(HttpStatusCode.OK, verified.Body?.ToJsonString());
        verified.Json["recoveryCodes"]!.AsArray().Count.ShouldBe(TwoFactorService.RecoveryCodeCount);

        status = await AdminFixture.SendAsync(client, HttpMethod.Get, "me/two-factor");
        status.Json["enabled"]!.GetValue<bool>().ShouldBeTrue();
        status.Json["pending"]!.GetValue<bool>().ShouldBeFalse();
        status.Json["recoveryCodesLeft"]!.GetValue<int>().ShouldBe(TwoFactorService.RecoveryCodeCount);
        (await AdminFixture.SendAsync(client, HttpMethod.Get, "me")).Json["twoFactorEnabled"]!.GetValue<bool>().ShouldBeTrue();

        var again = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/enrol");
        again.Status.ShouldBe(HttpStatusCode.Conflict);
        again.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.TwoFactorEnabled);

        // Turning it on gave a new security stamp, so the session signed in without it is not refreshed.
        using (var refreshed = await RefreshAsync(host, before.RefreshToken!))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var browser = new HostedPagesBrowser(host, "10.2.1.1");
        using var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
        password.Headers.Location!.OriginalString.ShouldStartWith("/account/two-factor?");
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
    }

    [Fact]
    public async Task ARecoveryCodeSignsInOnceAndIsThenSpent()
    {
        await using var host = TwoFactorHost();
        var editor = await CreateUserAsync(host);
        var (_, codes) = await EnableTwoFactorAsync(host, editor);

        using (var browser = new HostedPagesBrowser(host, "10.2.2.1"))
        {
            using var signedIn = await SignInWithRecoveryCodeAsync(browser, editor.Email, codes[0]);
            signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
        }

        using (var browser = new HostedPagesBrowser(host, "10.2.2.2"))
        {
            using var spent = await SignInWithRecoveryCodeAsync(browser, editor.Email, codes[0]);
            spent.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await spent.Content.ReadAsStringAsync(Token)).ShouldContain("The recovery code is not right");
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();

            using var other = await SignInWithRecoveryCodeAsync(browser, editor.Email, codes[1]);
            other.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        using var client = await host.AsUserAsync(editor);
        (await AdminFixture.SendAsync(client, HttpMethod.Get, "me/two-factor")).Json["recoveryCodesLeft"]!.GetValue<int>().ShouldBe(TwoFactorService.RecoveryCodeCount - 2);
    }

    [Fact]
    public async Task AUserWhoseRoleRequiresTwoFactorAndHasNoneIsRefusedASessionUntilEnrolled()
    {
        await using var host = TwoFactorHost();
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var role = await GuardedRoleAsync(admin);
        var user = await CreateUserAsync(host);
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"users/{user.Id:D}/roles/{role}")).Status.ShouldBe(HttpStatusCode.OK);

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var browser = new HostedPagesBrowser(host, "10.2.3.1");
        using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials(user.Email, Password));
        using var refused = await browser.FollowAsync(password, stopAt: "https://localhost/");
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The authorize endpoint issued no code: it sent the user to set up the second factor, and answers prompt=none so.
        var silent = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri, prompt: "none");
        using (var none = await browser.GetAsync(silent.Url))
        {
            QueryHelpers.ParseQuery(none.Headers.Location!.Query)["error"].ToString().ShouldBe("interaction_required");
        }

        using var authorize = await browser.GetAsync(request.Url);
        var enable = authorize.Headers.Location!.OriginalString;
        enable.ShouldStartWith("/account/enable-two-factor?ReturnUrl=");

        var page = await browser.PageAsync(enable);
        page.ShouldContain("otpauth://totp/");
        (await browser.PageAsync(enable)).ShouldContain(SharedKeyOf(page));
        var key = SharedKeyOf(page).Replace(" ", "", StringComparison.Ordinal);

        using var turnedOn = await browser.PostAsync(enable, page, new Dictionary<string, string> { ["Input.Code"] = Totp(key) });
        turnedOn.StatusCode.ShouldBe(HttpStatusCode.OK);
        var shown = await turnedOn.Content.ReadAsStringAsync(Token);
        shown.ShouldContain("Two-factor sign-in is on.");
        RecoveryCodes().Count(shown).ShouldBe(TwoFactorService.RecoveryCodeCount);

        using var done = await browser.FollowAsync(await browser.GetAsync(request.Url));
        var tokens = await request.ExchangeAsync(host, done);
        Amr(tokens.GetProperty("access_token").GetString()!).ShouldBe(["mfa"]);
    }

    [Fact]
    public async Task ATokenIssuedAfterTheChallengeCarriesTheMfaReference()
    {
        await using var host = TwoFactorHost();
        var secured = await CreateUserAsync(host);
        var (key, _) = await EnableTwoFactorAsync(host, secured);
        var plain = await CreateUserAsync(host);

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        JsonElement tokens;
        using (var browser = new HostedPagesBrowser(host, "10.2.4.1"))
        {
            using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials(secured.Email, Password));
            using var challenged = await browser.SubmitAsync(password.Headers.Location!.OriginalString, new Dictionary<string, string> { ["Input.Code"] = Totp(key) });
            using var done = await browser.FollowAsync(challenged);
            tokens = await request.ExchangeAsync(host, done);
        }

        Amr(tokens.GetProperty("access_token").GetString()!).ShouldBe(["mfa"]);
        Amr(tokens.GetProperty("id_token").GetString()!).ShouldBe(["mfa"]);
        using (var refreshed = await RefreshAsync(host, tokens.GetProperty("refresh_token").GetString()!))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync(Token));
            Amr(json.RootElement.GetProperty("access_token").GetString()!).ShouldBe(["mfa"]);
        }

        var other = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using (var browser = new HostedPagesBrowser(host, "10.2.4.2"))
        {
            using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(other.Url), Credentials(plain.Email, Password));
            using var done = await browser.FollowAsync(password);
            Amr((await other.ExchangeAsync(host, done)).GetProperty("access_token").GetString()!).ShouldBe(["pwd"]);
        }
    }

    [Fact]
    public async Task ARememberedDeviceDoesNotPassARoleThatRequiresTwoFactor()
    {
        await using var host = TwoFactorHost();
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(host);
        var (key, _) = await EnableTwoFactorAsync(host, editor);
        using var browser = new HostedPagesBrowser(host, "10.2.5.1");

        using (var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password)))
        {
            var twoFactor = password.Headers.Location!.OriginalString;
            (await browser.PageAsync(twoFactor)).ShouldContain("Do not ask again on this device");
            using var remembered = await browser.SubmitAsync(twoFactor, new Dictionary<string, string> { ["Input.Code"] = Totp(key), ["Input.RememberMachine"] = "true" });
            remembered.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            browser.HasCookie(IdentityConstants.TwoFactorRememberMeScheme).ShouldBeTrue();
        }

        await SignOutAsync(browser);

        // Not required yet: the remembered device skips the second factor, and the token says so.
        var first = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using (var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(first.Url), Credentials(editor.Email, Password)))
        {
            password.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
            using var signedIn = await browser.FollowAsync(password);
            var tokens = await first.ExchangeAsync(host, signedIn);
            Amr(tokens.GetProperty("access_token").GetString()!).ShouldBe(["pwd"]);

            var role = await GuardedRoleAsync(admin);
            (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"users/{editor.Id:D}/roles/{role}")).Status.ShouldBe(HttpStatusCode.OK);

            using var refreshed = await RefreshAsync(host, tokens.GetProperty("refresh_token").GetString()!);
            refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refreshed.Content.ReadAsStringAsync(Token)).ShouldContain("invalid_grant");
        }

        var silent = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri, prompt: "none");
        using (var none = await browser.GetAsync(silent.Url))
        {
            QueryHelpers.ParseQuery(none.Headers.Location!.Query)["error"].ToString().ShouldBe("login_required");
        }

        // The sign-in from the remembered device is not taken; the user signs in again and gives the second factor.
        var second = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var challenged = await browser.GetAsync(second.Url);
        challenged.Headers.Location!.AbsolutePath.ShouldBe("/account/login");
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();

        using var again = await browser.SubmitAsync(challenged.Headers.Location.PathAndQuery, Credentials(editor.Email, Password));
        var page = again.Headers.Location!.OriginalString;
        page.ShouldStartWith("/account/two-factor?");
        (await browser.PageAsync(page)).ShouldNotContain("Do not ask again on this device");

        using var passed = await browser.SubmitAsync(page, new Dictionary<string, string> { ["Input.Code"] = Totp(key), ["Input.RememberMachine"] = "true" });
        using var done = await browser.FollowAsync(passed);
        var final = await second.ExchangeAsync(host, done);
        Amr(final.GetProperty("access_token").GetString()!).ShouldBe(["mfa"]);
    }

    [Fact]
    public async Task AFlagWithoutAKeySendsTheUserToEnrolRatherThanRoundTheSignIn()
    {
        await using var host = TwoFactorHost();
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(host);
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"users/{editor.Id:D}/roles/{await GuardedRoleAsync(admin)}")).Status.ShouldBe(HttpStatusCode.OK);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.SetTwoFactorEnabledAsync((await users.FindByIdAsync(editor.Id.ToString()))!, true)).Succeeded.ShouldBeTrue();
        }

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var browser = new HostedPagesBrowser(host, "10.2.8.1");
        using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials(editor.Email, Password));
        password.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
        using var authorize = await browser.GetAsync(request.Url);
        authorize.Headers.Location!.OriginalString.ShouldStartWith("/account/enable-two-factor?");
        (await browser.PageAsync(authorize.Headers.Location.OriginalString)).ShouldContain("otpauth://totp/");
    }

    [Fact]
    public async Task TheSecondFactorOutlastsTheStampCheckOfTheCookie()
    {
        await using var host = TwoFactorHost();
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        var editor = await CreateUserAsync(host);
        var (key, _) = await EnableTwoFactorAsync(host, editor);
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"users/{editor.Id:D}/roles/{await GuardedRoleAsync(admin)}")).Status.ShouldBe(HttpStatusCode.OK);

        using var browser = new HostedPagesBrowser(host, "10.2.7.1");
        using (var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password)))
        using (var passed = await browser.SubmitAsync(password.Headers.Location!.OriginalString, new Dictionary<string, string> { ["Input.Code"] = Totp(key) }))
        {
            passed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        // Past the half hour after which Identity rebuilds the cookie's principal from the user.
        host.Time.Advance(TimeSpan.FromMinutes(31));
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var authorized = await browser.GetAsync(request.Url);
        Amr((await request.ExchangeAsync(host, authorized)).GetProperty("access_token").GetString()!).ShouldBe(["mfa"]);
    }

    [Fact]
    public async Task RolesSayWhetherTheyRequireTwoFactorAndSuperAdminMayRequireIt()
    {
        await using var host = TwoFactorHost();
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var created = await AdminFixture.SendAsync(admin, HttpMethod.Post, "roles", """{ "name": "Guarded", "requireTwoFactor": true, "grants": [ { "permission": "schema.read" } ] }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        var id = created.Json["id"]!.GetValue<string>();
        created.Json["requireTwoFactor"]!.GetValue<bool>().ShouldBeTrue();

        // Left out, the flag stays as it was; given, it changes.
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"roles/{id}", """{ "name": "Guarded", "grants": [] }""")).Json["requireTwoFactor"]!.GetValue<bool>().ShouldBeTrue();
        (await AdminFixture.SendAsync(admin, HttpMethod.Put, $"roles/{id}", """{ "name": "Guarded", "requireTwoFactor": false, "grants": [] }""")).Json["requireTwoFactor"]!.GetValue<bool>().ShouldBeFalse();
        (await AdminFixture.SendAsync(admin, HttpMethod.Get, "roles")).Json["items"]!.AsArray().ShouldAllBe(role => role!["requireTwoFactor"] != null);

        var superAdmin = (await AdminFixture.SendAsync(admin, HttpMethod.Get, $"roles/{BuiltInRoles.SuperAdmin:D}")).Json;
        superAdmin["requireTwoFactor"]!.GetValue<bool>().ShouldBeFalse();
        var grants = superAdmin["grants"]!.ToJsonString();

        var changed = await AdminFixture.SendAsync(admin, HttpMethod.Put, $"roles/{BuiltInRoles.SuperAdmin:D}", $$"""{ "name": "Super Admin", "description": "Something else", "requireTwoFactor": true, "grants": {{grants}} }""");
        changed.Status.ShouldBe(HttpStatusCode.Conflict);
        changed.Json["code"]!.GetValue<string>().ShouldBe(RoleErrorCodes.RoleFixed);

        var guarded = await AdminFixture.SendAsync(admin, HttpMethod.Put, $"roles/{BuiltInRoles.SuperAdmin:D}", $$"""{ "name": "Super Admin", "description": "Holds every permission.", "requireTwoFactor": true, "grants": {{grants}} }""");
        guarded.Status.ShouldBe(HttpStatusCode.OK, guarded.Body?.ToJsonString());
        guarded.Json["requireTwoFactor"]!.GetValue<bool>().ShouldBeTrue();
        guarded.Json["grants"]!.ToJsonString().ShouldBe(grants);

        // A new Super Admin, signed in with a password alone, gets no code.
        var other = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        (await Should.ThrowAsync<InvalidOperationException>(() => host.SignInAsync(other))).Message.ShouldContain("/account/enable-two-factor");
    }

    [Fact]
    public async Task TurningTwoFactorOffOrReplacingTheRecoveryCodesTakesACode()
    {
        await using var host = TwoFactorHost();
        var editor = await CreateUserAsync(host);
        var (key, codes) = await EnableTwoFactorAsync(host, editor);
        using var client = await host.AsUserAsync(editor);

        (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/recovery-codes", "{}")).Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var wrong = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/recovery-codes", Code(codes[0]));
        wrong.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidTwoFactorCode, "a recovery code does not replace the recovery codes");

        var replaced = await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/recovery-codes", Code(Totp(key)));
        replaced.Status.ShouldBe(HttpStatusCode.OK, replaced.Body?.ToJsonString());
        var fresh = replaced.Json["recoveryCodes"]!.AsArray().Select(code => code!.GetValue<string>()).ToList();
        fresh.ShouldNotContain(codes[0]);

        (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/disable", Code(codes[0]))).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidTwoFactorCode);
        (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/disable", Code(fresh[0].ToLowerInvariant()))).Status.ShouldBe(HttpStatusCode.NoContent);

        var status = await AdminFixture.SendAsync(client, HttpMethod.Get, "me/two-factor");
        status.Json["enabled"]!.GetValue<bool>().ShouldBeFalse();
        status.Json["pending"]!.GetValue<bool>().ShouldBeFalse("the key goes with it");
        (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/disable", Code(Totp(key)))).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.TwoFactorNotEnabled);
        (await AdminFixture.SendAsync(client, HttpMethod.Post, "me/two-factor/verify", Code(Totp(key)))).Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.NoAuthenticator);

        using var browser = new HostedPagesBrowser(host, "10.2.6.1");
        using var password = await browser.SubmitAsync("/account/login", Credentials(editor.Email, Password));
        password.Headers.Location!.OriginalString.ShouldBe("/");

        var key2 = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        using var asKey = host.AsKey(key2);
        (await AdminFixture.SendAsync(asKey, HttpMethod.Get, "me/two-factor")).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Each test signs in many times from few addresses, more than the auth limit allows in a minute.
    private FluentCmsTestHost TwoFactorHost() =>
        NewHost(_emails).WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000));

    private static async Task<(string Key, IReadOnlyList<string> Codes)> EnableTwoFactorAsync(FluentCmsTestHost host, TestUser user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        var twoFactor = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
        var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
        var setup = await twoFactor.EnrolAsync(stored, "FluentCMS");
        return (setup.SharedKey, await twoFactor.VerifyAsync(stored, Totp(setup.SharedKey)));
    }

    private static async Task<string> GuardedRoleAsync(HttpClient admin)
    {
        var created = await AdminFixture.SendAsync(admin, HttpMethod.Post, "roles", $$"""{ "name": "Guarded {{Guid.NewGuid():N}}", "requireTwoFactor": true, "grants": [ { "permission": "schema.read" } ] }""");
        created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        return created.Json["id"]!.GetValue<string>();
    }

    private static async Task<HttpResponseMessage> SignInWithRecoveryCodeAsync(HostedPagesBrowser browser, string userName, string code)
    {
        using var password = await browser.SubmitAsync("/account/login", Credentials(userName, Password));
        var returnUrl = QueryHelpers.ParseQuery(new Uri(OAuthBase, password.Headers.Location!).Query)["ReturnUrl"].ToString();
        return await browser.SubmitAsync("/account/recovery-code?ReturnUrl=" + Uri.EscapeDataString(returnUrl), new Dictionary<string, string> { ["Input.Code"] = code });
    }

    private static async Task<HttpResponseMessage> RefreshAsync(FluentCmsTestHost host, string refreshToken)
    {
        using var client = host.CreateSecureClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = FluentCmsTestHost.OAuthClientId,
        });
        return await client.PostAsync("/connect/token", content, Token);
    }

    // The amr of a JWT, which a single value may give as a string.
    private static string[] Amr(string jwt)
    {
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return payload.RootElement.TryGetProperty("amr", out var amr)
            ? amr.ValueKind == JsonValueKind.Array ? [.. amr.EnumerateArray().Select(item => item.GetString()!)] : [amr.GetString()!]
            : [];
    }

    private static string Code(string code) => JsonSerializer.Serialize(new { code });

    private static string WrongCode(string key) => Totp(key) == "000000" ? "111111" : "000000";

    private static string SharedKeyOf(string page) => WebUtility.HtmlDecode(SharedKey().Match(page).Groups[1].Value);

    [GeneratedRegex("<p><code>([a-z2-7 ]+)</code></p>")]
    private static partial Regex SharedKey();

    [GeneratedRegex("<li><code>[^<]+</code></li>")]
    private static partial Regex RecoveryCodes();
}
