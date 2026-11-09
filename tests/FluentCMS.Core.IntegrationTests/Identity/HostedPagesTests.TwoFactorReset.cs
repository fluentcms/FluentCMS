using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// An administrator resets another user's two-factor sign-in (#2851): only with users.manage, reach over the user's roles
// and a sign-in with a second factor of its own, and the reset signs the user out everywhere.
public sealed partial class HostedPagesTests
{
    [Fact]
    public async Task AnAdminWhoSignedInWithASecondFactorResetsAnotherUsersTwoFactorAndEndsItsSessions()
    {
        var host = _host;
        var admin = await CreateUserAsync(host, BuiltInRoles.SuperAdminName);
        var (adminKey, _) = await EnableTwoFactorAsync(host, admin);
        using var asAdmin = (await SignInWithSecondFactorAsync(host, admin, adminKey, "10.2.9.1")).Client;
        var role = await GuardedRoleAsync(asAdmin);

        var user = await CreateUserAsync(host);
        (await AdminFixture.SendAsync(asAdmin, HttpMethod.Put, $"users/{user.Id:D}/roles/{role}")).Status.ShouldBe(HttpStatusCode.OK);
        var (userKey, _) = await EnableTwoFactorAsync(host, user);
        var (asUser, tokens) = await SignInWithSecondFactorAsync(host, user, userKey, "10.2.9.2");
        using (asUser)
        {
            (await AdminFixture.SendAsync(asUser, HttpMethod.Get, "me")).Status.ShouldBe(HttpStatusCode.OK);

            var reset = await AdminFixture.SendAsync(asAdmin, HttpMethod.Post, $"users/{user.Id:D}/two-factor/reset");
            reset.Status.ShouldBe(HttpStatusCode.OK, reset.Body?.ToJsonString());
            reset.Json["twoFactorEnabled"]!.GetValue<bool>().ShouldBeFalse();

            // Its access token, refresh token and sessions are over.
            (await AdminFixture.SendAsync(asUser, HttpMethod.Get, "me")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var refreshed = await RefreshAsync(host, tokens.GetProperty("refresh_token").GetString()!))
        {
            refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await AdminFixture.SendAsync(asAdmin, HttpMethod.Get, $"users/{user.Id:D}/sessions")).Json["items"]!.AsArray().ShouldBeEmpty();

        // The key and the codes are gone, and the role that requires two-factor sends the user to enrol again.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            (await users.GetAuthenticatorKeyAsync(stored)).ShouldBeNull();
            (await users.CountRecoveryCodesAsync(stored)).ShouldBe(0);
        }

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using (var browser = new HostedPagesBrowser(host, "10.2.9.3"))
        {
            using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials(user.Email, Password));
            password.Headers.Location!.OriginalString.ShouldStartWith("/connect/authorize?");
            using var authorize = await browser.GetAsync(password.Headers.Location.OriginalString);
            authorize.Headers.Location!.OriginalString.ShouldStartWith("/account/enable-two-factor?ReturnUrl=");
        }

        var row = (await ActivityAsync(host, "user.two_factor_reset", count: 1, of: row => row.TargetId == user.Id.ToString("D"))).ShouldHaveSingleItem();
        row.TargetType.ShouldBe("user");
        row.TargetId.ShouldBe(user.Id.ToString("D"));
        row.ActorId.ShouldBe(admin.Id);
        row.ActorType.ShouldBe(PrincipalKind.AdminUser);
        row.SiteId.ShouldBeNull();
    }

    [Fact]
    public async Task AResetIsRefusedWithoutASecondFactorWithoutReachOverTheUsersRolesAndOnOneself()
    {
        var host = _host;
        var admin = await CreateUserAsync(host, BuiltInRoles.SuperAdminName);
        var (adminKey, _) = await EnableTwoFactorAsync(host, admin);
        using var withSecondFactor = (await SignInWithSecondFactorAsync(host, admin, adminKey, "10.2.10.1")).Client;
        using var withPasswordOnly = await host.AsUserAsync(admin);
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var asKey = host.AsKey(key);

        var manager = await CreateUserAsync(host, await RoleWithAsync(host, CorePermissions.UsersManage, "content.*.read"));
        var (managerKey, _) = await EnableTwoFactorAsync(host, manager);
        using var withoutReach = (await SignInWithSecondFactorAsync(host, manager, managerKey, "10.2.10.2")).Client;

        var user = await CreateUserAsync(host);
        await EnableTwoFactorAsync(host, user);
        var reset = $"users/{user.Id:D}/two-factor/reset";

        foreach (var client in new[] { withPasswordOnly, asKey })
        {
            var refused = await AdminFixture.SendAsync(client, HttpMethod.Post, reset);
            refused.Status.ShouldBe(HttpStatusCode.Forbidden, refused.Body?.ToJsonString());
            refused.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.SecondFactorRequired);
        }

        var beyond = await AdminFixture.SendAsync(withoutReach, HttpMethod.Post, reset);
        beyond.Status.ShouldBe(HttpStatusCode.Forbidden, beyond.Body?.ToJsonString());
        beyond.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");

        var self = await AdminFixture.SendAsync(withSecondFactor, HttpMethod.Post, $"users/{admin.Id:D}/two-factor/reset");
        self.Status.ShouldBe(HttpStatusCode.UnprocessableEntity, self.Body?.ToJsonString());
        self.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.InvalidUser);

        (await AdminFixture.SendAsync(withSecondFactor, HttpMethod.Get, $"users/{user.Id:D}")).Json["twoFactorEnabled"]!.GetValue<bool>().ShouldBeTrue();
        (await AdminFixture.SendAsync(withSecondFactor, HttpMethod.Post, reset)).Status.ShouldBe(HttpStatusCode.OK);

        var nothing = await AdminFixture.SendAsync(withSecondFactor, HttpMethod.Post, reset);
        nothing.Status.ShouldBe(HttpStatusCode.Conflict, nothing.Body?.ToJsonString());
        nothing.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.TwoFactorNotEnabled);
    }

    // The password and a code of the authenticator, so that the tokens carry amr=mfa.
    private static async Task<(HttpClient Client, JsonElement Tokens)> SignInWithSecondFactorAsync(FluentCmsTestHost host, TestUser user, string key, string address)
    {
        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        JsonElement tokens;
        using (var browser = new HostedPagesBrowser(host, address))
        {
            using var password = await browser.SubmitAsync("/account/login?ReturnUrl=" + Uri.EscapeDataString(request.Url), Credentials(user.Email, Password));
            using var challenged = await browser.SubmitAsync(password.Headers.Location!.OriginalString, new Dictionary<string, string> { ["Input.Code"] = Totp(key) });
            using var done = await browser.FollowAsync(challenged);
            tokens = await request.ExchangeAsync(host, done);
        }

        Amr(tokens.GetProperty("access_token").GetString()!).ShouldBe(["mfa"]);
        var client = host.CreateSecureClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        return (client, tokens);
    }

    private static async Task<string> RoleWithAsync(FluentCmsTestHost host, params string[] permissions)
    {
        var name = $"role-{Guid.NewGuid():N}";
        await host.CreateRoleAsync(name, permissions, Token);
        return name;
    }
}
