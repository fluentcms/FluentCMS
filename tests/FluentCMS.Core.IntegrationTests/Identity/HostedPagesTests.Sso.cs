using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using FluentCMS.Core.Api.Account;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Admin single sign-on (#2781) through the hosted login page, against an OpenID Connect provider in the process.
public sealed partial class HostedPagesTests
{
    private const string Corp = "corp";
    private const string Trusted = "corp-mfa";

    [Fact]
    public async Task AFirstSignInThroughAGenericOidcProviderCreatesTheUserWithTheDefaultRoleAndTheHostIssuesAToken()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        using var browser = new HostedPagesBrowser(host, "10.3.0.1");
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);

        using var challenge = await browser.GetAsync(request.Url);
        (await browser.PageAsync(challenge.Headers.Location!.PathAndQuery)).ShouldContain("Sign in with Corp");

        var ada = FakeIdentity.Of("ada@corp.example");
        using var callback = await SignInWithProviderAsync(browser, idp, ada, request.Url);
        callback.Headers.Location!.OriginalString.ShouldStartWith("/account/external-login?");
        using var done = await browser.FollowAsync(callback);
        var tokens = await request.ExchangeAsync(host, done);
        idp.TokenRequests.ShouldBe(1);

        var user = await LinkedUserAsync(host, Corp, ada.Subject);
        user.Email.ShouldBe("ada@corp.example");
        user.UserName.ShouldBe("ada@corp.example");
        user.PasswordHash.ShouldBeNull();
        (await RoleIdsAsync(host, user)).ShouldBe([BuiltInRoles.Author]);
        Claim(tokens.GetProperty("access_token").GetString()!, "sub").ShouldBe(user.Id.ToString("D"));

        // The second sign-in finds the user it linked.
        using (var again = new HostedPagesBrowser(host, "10.3.0.1"))
        {
            using var back = await SignInWithProviderAsync(again, idp, ada);
            (await ExternalLoginAsync(again, back)).ShouldBe("/");
            again.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
        }

        var rows = await ActivityAsync(host, "user.", count: 4);
        rows.Single(row => row.Action == "user.created").TargetId.ShouldBe(user.Id.ToString("D"));
        rows.Where(row => row.Action == "user.login_succeeded").ShouldAllBe(row => row.TargetId == user.Id.ToString("D") && row.Ip == "10.3.0.1");
        rows.Count(row => row.Action == "user.login_succeeded" && Details(row)["method"] == LoginMethods.External).ShouldBe(2);
    }

    [Fact]
    public async Task GroupClaimsGiveTheMappedRolesOnlyAndTheRolesFollowTheGroupsAtEverySignIn()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider =>
        {
            provider.RoleMappings.Add(new SsoRoleMapping { Group = "cms-editors", Role = BuiltInRoles.EditorName });
            provider.RoleMappings.Add(new SsoRoleMapping { Group = "cms-owners", Role = BuiltInRoles.SuperAdmin.ToString() });
            provider.RoleMappings.Add(new SsoRoleMapping { Group = "ghosts", Role = "No Such Role" });
        });
        var reviewer = await host.CreateRoleAsync("Reviewer", ["schema.read"], Token);

        // A group named like a role gives nothing; only the mapping of cms-editors applies, and a missing role is skipped.
        var grace = FakeIdentity.Of("grace@corp.example", "cms-editors", BuiltInRoles.SuperAdminName, "ghosts", BuiltInRoles.EditorName);
        using (var browser = new HostedPagesBrowser(host))
        {
            using var back = await SignInWithProviderAsync(browser, idp, grace);
            (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        }

        var user = await LinkedUserAsync(host, Corp, grace.Subject);
        (await RoleIdsAsync(host, user)).ShouldBe([BuiltInRoles.Author, BuiltInRoles.Editor], ignoreOrder: true);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.AddToRoleAsync((await users.FindByIdAsync(user.Id.ToString()))!, reviewer)).Succeeded.ShouldBeTrue();
        }

        // Out of cms-editors and into cms-owners: Editor goes, Super Admin comes because a mapping names it, and the default
        // role and the role an admin gave stay.
        using (var browser = new HostedPagesBrowser(host))
        {
            using var back = await SignInWithProviderAsync(browser, idp, grace with { Groups = ["cms-owners"] });
            (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        }

        (await RoleIdsAsync(host, user)).ShouldBe([BuiltInRoles.Author, BuiltInRoles.SuperAdmin, reviewer], ignoreOrder: true);
        var changes = await ActivityAsync(host, "user.role_changed", count: 2);
        changes.ShouldAllBe(row => row.TargetId == user.Id.ToString("D"));
    }

    [Fact]
    public async Task WithLocalLoginDisabledThePasswordFormIsNotOfferedAndAPasswordSignInIsRefused()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, disableLocalLogin: true);
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host, "10.3.0.9");

        var page = await browser.PageAsync("/account/login");
        page.ShouldNotContain("Input.Password");
        page.ShouldNotContain("Forgot your password?");
        page.ShouldContain("Sign in with Corp");

        using (var refused = await browser.PostAsync("/account/login", page, Credentials(editor.Email, Password)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain(LoginModel.Refused);
            browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var signIn = scope.ServiceProvider.GetRequiredService<CmsSignInManager>();
            var user = (await signIn.UserManager.FindByIdAsync(editor.Id.ToString()))!;
            (await signIn.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: false)).IsNotAllowed.ShouldBeTrue();
        }

        var failed = (await ActivityAsync(host, "user.login_failed", count: 1)).Single();
        Details(failed)["method"].ShouldBe(LoginMethods.Password);
        Details(failed)["reason"].ShouldBe(LoginFailureReasons.NotAllowed);
        failed.TargetId.ShouldBeNull();

        using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of("lin@corp.example"));
        (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeTrue();
    }

    [Fact]
    public async Task TheEntraIdOktaAndKeycloakPresetsResolveToTheOneOpenIdConnectHandler()
    {
        await using var web = new CmsWebHost();
        var app = web.Build(Environments.Development, new Dictionary<string, string?>
        {
            ["FluentCms:Sso:Providers:generic:Authority"] = "https://id.example.com/",
            ["FluentCms:Sso:Providers:generic:ClientId"] = "cms",
            ["FluentCms:Sso:Providers:entra:Preset"] = "EntraId",
            ["FluentCms:Sso:Providers:entra:TenantId"] = "contoso.onmicrosoft.com",
            ["FluentCms:Sso:Providers:entra:ClientId"] = "cms",
            ["FluentCms:Sso:Providers:okta:Preset"] = "Okta",
            ["FluentCms:Sso:Providers:okta:Domain"] = "dev-123.okta.com",
            ["FluentCms:Sso:Providers:okta:AuthorizationServerId"] = "default",
            ["FluentCms:Sso:Providers:okta:ClientId"] = "cms",
            ["FluentCms:Sso:Providers:keycloak:Preset"] = "Keycloak",
            ["FluentCms:Sso:Providers:keycloak:BaseUrl"] = "https://kc.example.com/",
            ["FluentCms:Sso:Providers:keycloak:Realm"] = "staff",
            ["FluentCms:Sso:Providers:keycloak:ClientId"] = "cms",
            ["FluentCms:Sso:Providers:keycloak:Scopes:0"] = "groups",
        });

        var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var expected = new Dictionary<string, (string DisplayName, string Authority)>
        {
            ["generic"] = ("generic", "https://id.example.com/"),
            ["entra"] = ("Microsoft Entra ID", "https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0"),
            ["okta"] = ("Okta", "https://dev-123.okta.com/oauth2/default"),
            ["keycloak"] = ("Keycloak", "https://kc.example.com/realms/staff"),
        };

        foreach (var (name, (displayName, authority)) in expected)
        {
            var scheme = (await schemes.GetSchemeAsync(name)).ShouldNotBeNull();
            scheme.HandlerType.ShouldBe(typeof(OpenIdConnectHandler));
            scheme.DisplayName.ShouldBe(displayName);

            var handler = options.Get(name);
            handler.Authority.ShouldBe(authority);
            handler.ClientId.ShouldBe("cms");
            handler.CallbackPath.Value.ShouldBe("/account/sso/" + name);
            handler.UsePkce.ShouldBeTrue();
            handler.ResponseType.ShouldBe("code");
            handler.SignInScheme.ShouldBe(Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme);
        }

        options.Get("keycloak").Scope.ShouldBe(["openid", "profile", "email", "groups"]);
        (await schemes.GetAllSchemesAsync()).Count(scheme => scheme.HandlerType == typeof(OpenIdConnectHandler)).ShouldBe(4);
    }

    [Fact]
    public async Task AnAddressThatBelongsToAUserNotLinkedToTheProviderIsNotTakenOver()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        var editor = await CreateUserAsync(host);
        using var browser = new HostedPagesBrowser(host);

        using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of(editor.Email));
        using var page = await browser.FollowAsync(back);

        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
        browser.HasCookie(".AspNetCore.Identity.Application").ShouldBeFalse();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.GetLoginsAsync((await users.FindByIdAsync(editor.Id.ToString()))!)).ShouldBeEmpty();
        }

        var failed = (await ActivityAsync(host, "user.login_failed", count: 1)).Single();
        Details(failed)["method"].ShouldBe(LoginMethods.External);
        Details(failed)["reason"].ShouldBe(LoginFailureReasons.NotAllowed);
        failed.TargetId.ShouldBeNull();
    }

    [Fact]
    public async Task WithoutAutoProvisioningSomeoneWithNoUserIsToldSo()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider => provider.AutoProvision = false);
        using var browser = new HostedPagesBrowser(host);
        var stranger = FakeIdentity.Of("stranger@corp.example");

        using var back = await SignInWithProviderAsync(browser, idp, stranger);
        using var page = await browser.FollowAsync(back);

        (await page.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByEmailAsync("stranger@corp.example")).ShouldBeNull();
        }

        Details((await ActivityAsync(host, "user.login_failed", count: 1)).Single())["reason"].ShouldBe(LoginFailureReasons.UnknownUser);
    }

    [Fact]
    public async Task AProviderErrorGoesBackToTheLoginPageAsAFailedSignIn()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        using var browser = new HostedPagesBrowser(host, "10.3.0.5");
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);

        using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of("ada@corp.example"), request.Url, error: "access_denied");

        back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var login = back.Headers.Location!.OriginalString;
        login.ShouldStartWith("/account/login?");
        var query = QueryHelpers.ParseQuery(new Uri(OAuthBase, login).Query);
        query["Status"].ToString().ShouldBe("external-failed");
        query["ReturnUrl"].ToString().ShouldBe(request.Url);
        (await browser.PageAsync(login)).ShouldContain("did not complete");
        idp.TokenRequests.ShouldBe(0);

        var failed = (await ActivityAsync(host, "user.login_failed", count: 1)).Single();
        Details(failed)["method"].ShouldBe(LoginMethods.External);
        failed.Ip.ShouldBe("10.3.0.5");
    }

    [Fact]
    public async Task UnderAPathBaseTheProviderCallbackAndTheExternalLoginPageCarryIt()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp).Configure(cms => cms.ReverseProxy.PathBase = "/cms");
        using var browser = new HostedPagesBrowser(host);

        using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of("pat@corp.example"), pathBase: "/cms");

        back.Headers.Location!.OriginalString.ShouldStartWith("/cms/account/external-login?");
        using var page = await browser.GetAsync(back.Headers.Location.OriginalString);
        page.Headers.Location!.OriginalString.ShouldBe("/cms/");

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var code = await browser.GetAsync("/cms" + request.Url);
        code.Headers.Location!.AbsoluteUri.ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
    }

    // An external sign-in carries no amr of its own (#2812), so a role that requires two-factor asks for the local second
    // factor after it, unless the provider is trusted for its amr and says mfa.
    [Fact]
    public async Task ARoleThatRequiresTwoFactorTakesTheProvidersMfaOnlyFromAProviderTrustedForIt()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider => provider.RoleMappings.Add(new SsoRoleMapping { Group = "guarded", Role = "Guarded" }));
        using (var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)))
        {
            var created = await AdminFixture.SendAsync(admin, HttpMethod.Post, "roles", """{ "name": "Guarded", "requireTwoFactor": true, "grants": [ { "permission": "schema.read" } ] }""");
            created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        }

        var withMfa = new FakeIdentity("sub-untrusted", "una@corp.example", ["guarded"], ["pwd", "mfa"]);
        (await AuthorizeThroughProviderAsync(host, idp, Corp, withMfa)).ShouldStartWith("/account/enable-two-factor?");

        var trustedWithoutMfa = new FakeIdentity("sub-trusted-pwd", "tess@corp.example", ["guarded"], ["pwd"]);
        (await AuthorizeThroughProviderAsync(host, idp, Trusted, trustedWithoutMfa)).ShouldStartWith("/account/enable-two-factor?");

        var trustedWithMfa = new FakeIdentity("sub-trusted-mfa", "tim@corp.example", ["guarded"], ["pwd", "mfa"]);
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var browser = new HostedPagesBrowser(host);
        using var back = await SignInWithProviderAsync(browser, idp, trustedWithMfa, request.Url, provider: Trusted);
        var tokens = await request.ExchangeAsync(host, await browser.FollowAsync(back));
        Amr(tokens.GetProperty("access_token").GetString()!).ShouldContain("mfa");
    }

    // Where the external login page sends the browser that the provider's callback sent there.
    private static async Task<string> ExternalLoginAsync(HostedPagesBrowser browser, HttpResponseMessage callback)
    {
        callback.Headers.Location!.OriginalString.ShouldStartWith("/account/external-login?");
        using var page = await browser.GetAsync(callback.Headers.Location.OriginalString);
        page.StatusCode.ShouldBe(HttpStatusCode.Redirect, await page.Content.ReadAsStringAsync(Token));
        return page.Headers.Location!.OriginalString;
    }

    // Where the authorize request goes after the sign-in through the provider.
    private static async Task<string> AuthorizeThroughProviderAsync(FluentCmsTestHost host, FakeOidcProvider idp, string provider, FakeIdentity user)
    {
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var browser = new HostedPagesBrowser(host);
        using var back = await SignInWithProviderAsync(browser, idp, user, request.Url, provider: provider);
        using var callback = await browser.GetAsync(back.Headers.Location!.OriginalString);
        callback.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
        using var authorize = await browser.GetAsync(callback.Headers.Location.OriginalString);
        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return authorize.Headers.Location!.OriginalString;
    }

    // What the browser does: the login page's button for the provider, the provider's authorize endpoint, and the form post
    // of the provider's answer back to the callback path. Returns the callback's answer.
    private static async Task<HttpResponseMessage> SignInWithProviderAsync(
        HostedPagesBrowser browser, FakeOidcProvider idp, FakeIdentity user, string? returnUrl = null, string? error = null, string provider = Corp, string pathBase = "")
    {
        var login = await browser.PageAsync(pathBase + "/account/login" + (returnUrl is null ? "" : "?ReturnUrl=" + Uri.EscapeDataString(returnUrl)));
        using var challenge = await browser.PostAsync(pathBase + "/account/login?handler=External", login, new Dictionary<string, string> { ["provider"] = provider, ["ReturnUrl"] = returnUrl ?? "" });
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync(Token));
        var authorize = challenge.Headers.Location!;

        var answer = idp.Authorize(authorize, user, error);
        var callback = new Uri(QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString());
        callback.AbsoluteUri.ShouldBe($"https://localhost{pathBase}/account/sso/{provider}");
        return await browser.PostAsync(callback.PathAndQuery, answer);
    }

    private FluentCmsTestHost SsoHost(FakeOidcProvider idp, Action<SsoProvider>? configure = null, bool disableLocalLogin = false) =>
        NewHost(_emails)
            .WithServices(services =>
            {
                services.PostConfigure<RateLimitOptions>(limits => limits.Auth.PermitLimit = 10_000);
                services.ConfigureAll<OpenIdConnectOptions>(options => options.BackchannelHttpHandler = idp.Handler);
            })
            .Configure(cms =>
            {
                cms.Sso.DisableLocalLogin = disableLocalLogin;
                foreach (var (name, trusted) in new[] { (Corp, false), (Trusted, true) })
                {
                    var provider = new SsoProvider
                    {
                        Authority = idp.Issuer,
                        ClientId = FakeOidcProvider.ClientId,
                        ClientSecret = FakeOidcProvider.ClientSecret,
                        DisplayName = trusted ? "Corp MFA" : "Corp",
                        DefaultRole = BuiltInRoles.AuthorName,
                        TrustMfaClaim = trusted,
                    };
                    configure?.Invoke(provider);
                    cms.Sso.Providers[name] = provider;
                }
            });

    private static async Task<CmsUser> LinkedUserAsync(FluentCmsTestHost host, string provider, string subject)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByLoginAsync(provider, subject)).ShouldNotBeNull();
    }

    private static async Task<IReadOnlyList<Guid>> RoleIdsAsync(FluentCmsTestHost host, CmsUser user)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        return await users.GetRoleIdsAsync((await users.FindByIdAsync(user.Id.ToString()))!);
    }

    private static string Claim(string jwt, string name)
    {
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return payload.RootElement.GetProperty(name).GetString()!;
    }
}
