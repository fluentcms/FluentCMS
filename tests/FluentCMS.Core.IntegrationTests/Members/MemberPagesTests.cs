using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// The member pages at /account/members, driven the way a browser drives them, against the real authorize and token
// endpoints.
public sealed class MemberPagesTests(MemberFixture fixture) : IClassFixture<MemberFixture>
{
    private const string Password = MemberFixture.Password;
    private const string AdminPassword = "An-Administrators-Passphrase-3";
    private const string AdminCookie = ".AspNetCore.Identity.Application";
    private const string MemberCookie = ".FluentCms.Members";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AVisitorRegistersVerifiesAndSignsInThroughTheHostedPagesWithTheCodeFlowAndPkce()
    {
        var address = MemberFixture.NewAddress();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.1");
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid profile email offline_access");

        // The member client's request goes to the member login page, which offers the registration.
        using var challenge = await browser.GetAsync(request.Url);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = challenge.Headers.Location!;
        login.AbsolutePath.ShouldBe("/account/members/login");
        (await browser.PageAsync(login.PathAndQuery)).ShouldContain("/account/members/register");

        using (var registered = await browser.SubmitAsync("/account/members/register", new Dictionary<string, string>
        {
            ["Input.Email"] = address,
            ["Input.DisplayName"] = "Page Member",
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        }))
        {
            registered.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await registered.Content.ReadAsStringAsync(Token)).ShouldContain("an email with a link to verify it is on its way");
        }

        // Not verified yet: the login page refuses, with the answer it gives a wrong password.
        using (var early = await browser.SubmitAsync(login.PathAndQuery, MemberTokenTests.Credentials(address, Password)))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await early.Content.ReadAsStringAsync(Token)).ShouldContain("Sign-in failed.");
            browser.HasCookie(MemberCookie).ShouldBeFalse();
        }

        // Opening the link changes nothing; the button on the page verifies.
        var link = fixture.Emails.LinkTo(address);
        link.AbsolutePath.ShouldBe("/account/members/verify-email");
        (await browser.PageAsync(link.PathAndQuery)).ShouldContain("Verify the address");
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);
        using (var verified = await browser.SubmitAsync(link.PathAndQuery, new Dictionary<string, string>()))
        {
            verified.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            verified.Headers.Location!.OriginalString.ShouldBe("/account/members/login?Status=email-verified");
        }

        using var signedIn = await browser.SubmitAsync(login.PathAndQuery, MemberTokenTests.Credentials(address, Password));
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
        browser.HasCookie(MemberCookie).ShouldBeTrue();
        browser.HasCookie(AdminCookie).ShouldBeFalse();

        using var done = await browser.FollowAsync(signedIn);
        var tokens = await request.ExchangeAsync(fixture.Host, done);
        var access = MemberTokenTests.Payload(tokens.GetProperty("access_token").GetString()!);
        MemberTokenTests.Audiences(access).ShouldBe(["fluentcms:members"]);
        access.GetProperty("fcms_kind").GetString().ShouldBe("Member");
        access.GetProperty("name").GetString().ShouldBe("Page Member");
        tokens.GetProperty("refresh_token").GetString().ShouldNotBeNullOrEmpty();

        using var member = fixture.Bearer(tokens.GetProperty("access_token").GetString()!);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: member)).Json["email"]!.GetValue<string>().ShouldBe(address);
        (await MemberFixture.TitlesAsync(member, "perk")).Titles.ShouldBe(["Standard perk"]);

        // The verification link is spent.
        using var again = await browser.SubmitAsync(link.PathAndQuery, new Dictionary<string, string>());
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadAsStringAsync(Token)).ShouldContain("The link is not valid");
    }

    [Fact]
    public async Task AnAdminSessionAndAMemberSessionInOneBrowserNeitherSignEachOtherInNorOut()
    {
        var admin = await CreateAdminAsync();
        var address = await fixture.RegisterVerifiedAsync();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.2");
        var adminRequest = "/connect/authorize" + AuthorizeRequest.Query(FluentCmsTestHost.OAuthClientId, "openid management");
        var memberRequest = "/connect/authorize" + AuthorizeRequest.Query(FluentCmsTestHost.OAuthMemberClientId, "openid");

        // The admin signs in: the member client is still asked to sign in, on the member pages.
        await SignInAdminAsync(browser, admin.Email);
        browser.HasCookie(AdminCookie).ShouldBeTrue();
        browser.HasCookie(MemberCookie).ShouldBeFalse();
        (await LocationAsync(browser, adminRequest)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
        (await LocationAsync(browser, memberRequest)).ShouldContain("/account/members/login");

        // The member signs in beside it: each client gets its own pool's code.
        await SignInMemberAsync(browser, address);
        browser.HasCookie(AdminCookie).ShouldBeTrue();
        browser.HasCookie(MemberCookie).ShouldBeTrue();
        (await LocationAsync(browser, adminRequest)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
        (await LocationAsync(browser, memberRequest)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");

        // The member signs out on its page: the admin session goes on.
        var memberLogout = await browser.PageAsync("/account/members/logout");
        memberLogout.ShouldContain(address);
        using (var signedOut = await browser.PostAsync("/account/members/logout", memberLogout, new Dictionary<string, string>()))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        browser.HasCookie(MemberCookie).ShouldBeFalse();
        browser.HasCookie(AdminCookie).ShouldBeTrue();
        (await LocationAsync(browser, adminRequest)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
        (await LocationAsync(browser, memberRequest)).ShouldContain("/account/members/login");

        // The admin signs out on its page: the member session goes on.
        await SignInMemberAsync(browser, address);
        var adminLogout = await browser.PageAsync("/account/logout");
        using (var signedOut = await browser.PostAsync("/connect/logout", adminLogout, new Dictionary<string, string>()))
        {
            signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        browser.HasCookie(AdminCookie).ShouldBeFalse();
        browser.HasCookie(MemberCookie).ShouldBeTrue();
        (await LocationAsync(browser, memberRequest)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
        (await LocationAsync(browser, adminRequest)).ShouldContain("/account/login");
    }

    [Fact]
    public async Task ALogoutPageConfirmsOnlyTheEndOfItsOwnPoolsSignIn()
    {
        var admin = await CreateAdminAsync();
        var address = await fixture.RegisterVerifiedAsync();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.3");
        await SignInAdminAsync(browser, admin.Email);
        await SignInMemberAsync(browser, address);

        // A link to the member logout page that names the admin client: its button does not end the admin session.
        var crafted = $"/account/members/logout?client_id={FluentCmsTestHost.OAuthClientId}";
        var memberPage = await browser.PageAsync(crafted);
        using (var posted = await browser.PostAsync("/connect/logout", memberPage, new Dictionary<string, string>
        {
            ["client_id"] = FluentCmsTestHost.OAuthClientId,
            [MemberAuthentication.LogoutPageField] = MemberAuthentication.LogoutPageMark,
        }))
        {
            posted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            posted.Headers.Location!.OriginalString.ShouldStartWith("/account/logout?");
            posted.Headers.Location!.OriginalString.ShouldNotContain(MemberAuthentication.LogoutPageField);
        }

        browser.HasCookie(AdminCookie).ShouldBeTrue();
        browser.HasCookie(MemberCookie).ShouldBeTrue();

        // A link to the admin logout page that names the member client and forges the member page's mark: the page does not
        // pass the mark on, and without it the button does not end the member session.
        var forged = $"/account/logout?client_id={FluentCmsTestHost.OAuthMemberClientId}&{MemberAuthentication.LogoutPageField}={MemberAuthentication.LogoutPageMark}";
        var adminPage = await browser.PageAsync(forged);
        adminPage.ShouldNotContain(MemberAuthentication.LogoutPageField);
        using (var posted = await browser.PostAsync("/connect/logout", adminPage, new Dictionary<string, string> { ["client_id"] = FluentCmsTestHost.OAuthMemberClientId }))
        {
            posted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            posted.Headers.Location!.OriginalString.ShouldStartWith("/account/members/logout?");
        }

        browser.HasCookie(AdminCookie).ShouldBeTrue();
        browser.HasCookie(MemberCookie).ShouldBeTrue();

        // The member client's own logout request, confirmed on the member page, ends the member session alone.
        using (var asked = await browser.GetAsync($"/connect/logout?client_id={FluentCmsTestHost.OAuthMemberClientId}"))
        {
            asked.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            asked.Headers.Location!.OriginalString.ShouldStartWith("/account/members/logout?");
            var page = await browser.PageAsync(asked.Headers.Location!.OriginalString);
            using var confirmed = await browser.PostAsync("/connect/logout", page, new Dictionary<string, string>
            {
                ["client_id"] = FluentCmsTestHost.OAuthMemberClientId,
                [MemberAuthentication.LogoutPageField] = MemberAuthentication.LogoutPageMark,
            });
            confirmed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        browser.HasCookie(MemberCookie).ShouldBeFalse();
        browser.HasCookie(AdminCookie).ShouldBeTrue();

        // A sign-out by GET changes nothing.
        await SignInMemberAsync(browser, address);
        using (var byGet = await browser.GetAsync("/account/members/logout"))
        {
            byGet.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        browser.HasCookie(MemberCookie).ShouldBeTrue();
    }

    [Fact]
    public async Task AMembersIdTokenAsAHintEndsTheMemberSessionAndLeavesTheAdmins()
    {
        var admin = await CreateAdminAsync();
        var address = await fixture.RegisterVerifiedAsync();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.4");
        await SignInAdminAsync(browser, admin.Email);
        await SignInMemberAsync(browser, address);

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthMemberClientId, "openid");
        using var code = await browser.GetAsync(request.Url);
        var tokens = await request.ExchangeAsync(fixture.Host, code);

        using var signedOut = await browser.GetAsync("/connect/logout?id_token_hint=" + Uri.EscapeDataString(tokens.GetProperty("id_token").GetString()!));
        signedOut.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        browser.HasCookie(MemberCookie).ShouldBeFalse();
        browser.HasCookie(AdminCookie).ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    public async Task AfterSigningInAMemberReturnsOnlyToAPathOfTheHost(string returnUrl)
    {
        var address = await fixture.RegisterVerifiedAsync();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.5");

        using var signedIn = await browser.SubmitAsync("/account/members/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), MemberTokenTests.Credentials(address, Password));

        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signedIn.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task TheMemberPagesRefuseAPostWithoutTheirAntiforgeryTokenAndRunNoScript()
    {
        var address = await fixture.RegisterVerifiedAsync();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.6");

        foreach (var page in (string[])["login", "register", "forgot-password", "verify-email", "reset-password?email=a%40example.com&token=t", "logout"])
        {
            using var response = await browser.GetAsync("/account/members/" + page);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, page);
            response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem().ShouldContain("default-src 'none'");
            response.Headers.GetValues("Referrer-Policy").ShouldHaveSingleItem().ShouldBe("no-referrer");

            var path = "/account/members/" + page.Split('?')[0];
            using var forged = await browser.PostAsync(path, MemberTokenTests.Credentials(address, Password));
            forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest, page);
        }

        browser.HasCookie(MemberCookie).ShouldBeFalse();
    }

    [Fact]
    public async Task TheLoginPageGivesEveryRefusalTheSameAnswerAndWritesActivityRows()
    {
        var address = await fixture.RegisterVerifiedAsync();
        var pending = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{pending}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.7");
        var page = await browser.PageAsync("/account/members/login");

        List<string> answers = [];
        foreach (var (email, password) in ((string, string)[])[(address, "Not-The-Password-1"), ("nobody@example.com", Password), (pending, Password)])
        {
            using var refused = await browser.PostAsync("/account/members/login", page, MemberTokenTests.Credentials(email, password));
            refused.StatusCode.ShouldBe(HttpStatusCode.OK);
            var html = await refused.Content.ReadAsStringAsync(Token);
            html.ShouldContain(WebUtility.HtmlEncode(MemberLoginModel.Refused));
            answers.Add(html.Replace(WebUtility.HtmlEncode(email), "", StringComparison.Ordinal).Replace(email, "", StringComparison.Ordinal));
        }

        answers.Select(Strip).Distinct().Count().ShouldBe(1);
        browser.HasCookie(MemberCookie).ShouldBeFalse();

        using (var right = await browser.PostAsync("/account/members/login", page, MemberTokenTests.Credentials(address, Password)))
        {
            right.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var id = (await fixture.MemberIdAsync(address)).ToString("D");
        var rows = (await fixture.ActivityAsync("member.login_")).Where(row => row.Ip == "10.8.0.7").ToList();
        var failed = rows.Single(row => row.Action == "member.login_failed" && row.TargetId == id);
        failed.ActorType.ShouldBe(PrincipalKind.Anonymous);
        failed.SiteId.ShouldBeNull();
        Details(failed.Details)["reason"].ShouldBe(LoginFailureReasons.InvalidCredentials);
        Details(failed.Details)["method"].ShouldBe(MemberLoginMethods.Password);

        var stranger = rows.Single(row => row.Action == "member.login_failed" && row.TargetId is null);
        Details(stranger.Details)["reason"].ShouldBe(LoginFailureReasons.UnknownUser);
        stranger.Details!.ShouldNotContain("nobody@example.com");

        Details(rows.Single(row => row.Action == "member.login_failed" && row.TargetId is not null && row.TargetId != id).Details)["reason"].ShouldBe(LoginFailureReasons.NotAllowed);

        var succeeded = rows.Single(row => row.Action == "member.login_succeeded");
        succeeded.ActorType.ShouldBe(PrincipalKind.Member);
        succeeded.ActorId.ToString("D").ShouldBe(id);
        Details(succeeded.Details)["method"].ShouldBe(MemberLoginMethods.Password);
    }

    [Fact]
    public async Task AMemberResetsAForgottenPasswordOnThePages()
    {
        var address = await fixture.RegisterVerifiedAsync();
        fixture.Host.Time.Advance(MemberMailLimiter.Window + TimeSpan.FromSeconds(1));
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.8");

        // A known and an unknown address get the same page.
        string known, unknown;
        using (var sent = await browser.SubmitAsync("/account/members/forgot-password", new Dictionary<string, string> { ["Input.Email"] = address }))
        {
            known = await sent.Content.ReadAsStringAsync(Token);
        }

        using (var sent = await browser.SubmitAsync("/account/members/forgot-password", new Dictionary<string, string> { ["Input.Email"] = "nobody@example.com" }))
        {
            unknown = await sent.Content.ReadAsStringAsync(Token);
        }

        Strip(known).ShouldBe(Strip(unknown));
        known.ShouldContain("an email with a link to choose a new password is on its way");

        var link = fixture.Emails.LinkTo(address);
        link.AbsolutePath.ShouldBe("/account/members/reset-password");
        using (var reset = await browser.SubmitAsync(link.PathAndQuery, new Dictionary<string, string>
        {
            ["Input.Password"] = MemberFixture.NewPassword,
            ["Input.ConfirmPassword"] = MemberFixture.NewPassword,
        }))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            reset.Headers.Location!.OriginalString.ShouldBe("/account/members/login?Status=password-reset");
        }

        using (var old = await browser.SubmitAsync("/account/members/login", MemberTokenTests.Credentials(address, Password)))
        {
            old.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var signedIn = await browser.SubmitAsync("/account/members/login", MemberTokenTests.Credentials(address, MemberFixture.NewPassword));
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        // The link is spent.
        using var again = await browser.SubmitAsync(link.PathAndQuery, new Dictionary<string, string>
        {
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        });
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadAsStringAsync(Token)).ShouldContain("The link is not valid");
    }

    [Fact]
    public async Task APasswordChangeEndsTheMemberCookieToo()
    {
        var address = await fixture.RegisterVerifiedAsync();
        using var browser = new HostedPagesBrowser(fixture.Host, "10.8.0.9");
        await SignInMemberAsync(browser, address);
        var memberRequest = "/connect/authorize" + AuthorizeRequest.Query(FluentCmsTestHost.OAuthMemberClientId, "openid");
        (await LocationAsync(browser, memberRequest)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");

        var tokens = await fixture.DirectLoginAsync(address, Password);
        using var client = fixture.Bearer(tokens.Json["access_token"]!.GetValue<string>());
        (await fixture.MembersAsync(HttpMethod.Post, "me/change-password", $$"""{ "currentPassword": "{{Password}}", "newPassword": "{{MemberFixture.NewPassword}}" }""", client)).Status.ShouldBe(HttpStatusCode.NoContent);

        // The cookie carries the security stamp the password change replaced.
        (await LocationAsync(browser, memberRequest)).ShouldContain("/account/members/login");
    }

    [Fact]
    public async Task TheMemberRoutesAndPagesTakeTheAuthLimitByClientAddress()
    {
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails)
            .WithServices(services => services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 3));
        using var one = new HostedPagesBrowser(host, "10.8.1.1");
        using var other = new HostedPagesBrowser(host, "10.8.1.2");

        for (var call = 0; call < 3; call++)
        {
            using var content = new StringContent("""{ "email": "nobody@example.com" }""", Encoding.UTF8, "application/json");
            using var allowed = await one.SendAsync(HttpMethod.Post, MemberFixture.Api + "forgot-password", content);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using (var content = new StringContent("""{ "email": "nobody@example.com" }""", Encoding.UTF8, "application/json"))
        {
            using var limited = await one.SendAsync(HttpMethod.Post, MemberFixture.Api + "forgot-password", content);
            limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter.ShouldNotBeNull();
        }

        // The pages count in the same bucket of the address, and another address has its own.
        using (var page = await one.GetAsync("/account/members/login"))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        }

        using (var page = await other.GetAsync("/account/members/login"))
        {
            page.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task TheMemberRoutesAndTheAuthorizationServerAnswerTheMemberOriginsOnly()
    {
        const string App = "https://app.example.com";
        const string AdminApp = "https://admin.example.com";
        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails)
            .WithServices(services => services.PostConfigure<SecurityOptions>(security =>
            {
                security.Cors.Members.AllowedOrigins.Add(App);
                security.Cors.Management.AllowedOrigins.Add(AdminApp);
            }));
        using var client = host.CreateSecureClient();

        (await PreflightAsync(client, MemberFixture.Api + "register", App, "POST")).ShouldBe(App);
        (await PreflightAsync(client, MemberFixture.Api + "me", App, "PATCH")).ShouldBe(App);
        (await PreflightAsync(client, MemberFixture.Api + "register", AdminApp, "POST")).ShouldBeNull();
        (await PreflightAsync(client, MemberFixture.Api + "register", "https://evil.example", "POST")).ShouldBeNull();

        // /connect and the discovery document answer the member origins beside the Management ones.
        (await PreflightAsync(client, "/connect/token", App, "POST")).ShouldBe(App);
        (await PreflightAsync(client, "/connect/token", AdminApp, "POST")).ShouldBe(AdminApp);
        (await PreflightAsync(client, "/.well-known/openid-configuration", App, "GET")).ShouldBe(App);
        (await PreflightAsync(client, "/connect/token", "https://evil.example", "POST")).ShouldBeNull();

        // The Management API does not answer a member origin.
        (await PreflightAsync(client, MemberFixture.Management + "members", App, "GET")).ShouldBeNull();
        (await PreflightAsync(client, MemberFixture.Management + "members", AdminApp, "GET")).ShouldBe(AdminApp);

        // No credentials are allowed, so a browser sends no cookie to the member routes.
        using var request = new HttpRequestMessage(HttpMethod.Options, MemberFixture.Api + "register");
        request.Headers.Add("Origin", App);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(request, Token);
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();
    }

    private static async Task<string?> PreflightAsync(HttpClient client, string url, string origin, string method)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, url);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        using var response = await client.SendAsync(request, Token);
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
    }

    private async Task<TestUser> CreateAdminAsync()
    {
        var admin = await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        (await users.AddPasswordAsync((await users.FindByIdAsync(admin.Id.ToString()))!, AdminPassword)).Succeeded.ShouldBeTrue();
        return admin;
    }

    private static async Task SignInAdminAsync(HostedPagesBrowser browser, string userName)
    {
        using var response = await browser.SubmitAsync("/account/login", new Dictionary<string, string> { ["Input.UserName"] = userName, ["Input.Password"] = AdminPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    private static async Task SignInMemberAsync(HostedPagesBrowser browser, string address)
    {
        using var response = await browser.SubmitAsync("/account/members/login", MemberTokenTests.Credentials(address, Password));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    // Where an authorize request sends the browser: the client's redirect URI with a code, or a login page.
    private static async Task<string> LocationAsync(HostedPagesBrowser browser, string url)
    {
        using var response = await browser.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return response.Headers.Location!.OriginalString;
    }

    private static Dictionary<string, string?> Details(string? details) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(details ?? "{}")!;

    // A page without what differs from request to request: the antiforgery token.
    private static string Strip(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "name=\"__RequestVerificationToken\"[^>]*>", "");

    // An authorization code request with PKCE, and the exchange of the code it brings back.
    private sealed record AuthorizeRequest(string Url, string ClientId, string Verifier)
    {
        public static AuthorizeRequest For(string clientId, string scope)
        {
            var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            return new AuthorizeRequest("/connect/authorize" + Query(clientId, scope, verifier), clientId, verifier);
        }

        public static QueryString Query(string clientId, string scope, string? verifier = null) =>
            QueryString.Create(new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["response_type"] = "code",
                ["redirect_uri"] = FluentCmsTestHost.OAuthRedirectUri,
                ["scope"] = scope,
                ["code_challenge"] = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier ?? "a-verifier-nobody-redeems-a-code-with"))),
                ["code_challenge_method"] = "S256",
                ["state"] = "test",
            });

        public async Task<JsonElement> ExchangeAsync(FluentCmsTestHost host, HttpResponseMessage redirect)
        {
            redirect.StatusCode.ShouldBe(HttpStatusCode.Redirect, await redirect.Content.ReadAsStringAsync(Token));
            var location = redirect.Headers.Location!;
            location.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri);
            var code = QueryHelpers.ParseQuery(location.Query)["code"].ToString();
            code.ShouldNotBeNullOrEmpty();

            using var client = host.CreateSecureClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = FluentCmsTestHost.OAuthRedirectUri,
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
}
