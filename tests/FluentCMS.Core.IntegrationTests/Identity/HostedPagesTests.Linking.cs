using System.Net;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Management;
using FluentCMS.Core.Sites;
using FluentCMS.Events;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Invited and existing users linked to external logins (#2818), and the logins of a user over the Management API.
public sealed partial class HostedPagesTests
{
    private const string ApplicationCookie = ".AspNetCore.Identity.Application";

    [Fact]
    public async Task WithLocalLoginDisabledAnInvitedUserAcceptsThroughAProviderAndSignsInWithItAfterwards()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, disableLocalLogin: true);
        var link = await InviteAsync(host, "ada@corp.example");
        _emails.Sent.Last().TextBody.ShouldContain("by signing in with your organization's account");
        using var browser = new HostedPagesBrowser(host, "10.3.1.1");

        var page = await browser.PageAsync(link.PathAndQuery);
        page.ShouldContain("Accept with Corp");
        page.ShouldNotContain("Input.Password");
        using (var password = await browser.PostAsync(link.PathAndQuery, page, new Dictionary<string, string>
        {
            ["Input.UserName"] = "ada",
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
        }))
        {
            (await password.Content.ReadAsStringAsync(Token)).ShouldContain("Sign-in with a password is turned off here.");
        }

        // The provider's address differs from the invitation's only in case.
        var ada = FakeIdentity.Of("Ada@Corp.example");
        using (var accepted = await AcceptWithProviderAsync(browser, idp, link, ada))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            accepted.Headers.Location!.OriginalString.ShouldBe("/");
        }

        browser.HasCookie(ApplicationCookie).ShouldBeTrue();
        var user = await LinkedUserAsync(host, Corp, ada.Subject);
        user.Email.ShouldBe("ada@corp.example");
        user.UserName.ShouldBe("ada@corp.example");
        user.PasswordHash.ShouldBeNull();
        (await RoleIdsAsync(host, user)).ShouldBe([BuiltInRoles.Editor]);
        (await browser.PageAsync(link.PathAndQuery)).ShouldContain("This invitation is not valid");

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var later = new HostedPagesBrowser(host, "10.3.1.2");
        using var back = await SignInWithProviderAsync(later, idp, ada, request.Url);
        var tokens = await request.ExchangeAsync(host, await later.FollowAsync(back));
        Claim(tokens.GetProperty("access_token").GetString()!, "sub").ShouldBe(user.Id.ToString("D"));

        var linked = (await ActivityAsync(host, "user.login_linked", count: 1)).Single();
        linked.TargetId.ShouldBe(user.Id.ToString("D"));
        Details(linked)["provider"].ShouldBe(Corp);
        (await ActivityAsync(host, "user.", count: 5)).Count(row => row.Action == "user.login_succeeded" && row.TargetId == user.Id.ToString("D")).ShouldBe(2);
    }

    [Fact]
    public async Task AnInvitationIsNotAcceptedThroughAProviderWhoseAddressIsAnotherOrNotVerified()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        var link = await InviteAsync(host, "grace@corp.example");
        using var browser = new HostedPagesBrowser(host);
        (await browser.PageAsync(link.PathAndQuery)).ShouldContain("Input.Password");

        using (var other = await AcceptWithProviderAsync(browser, idp, link, FakeIdentity.Of("mallory@corp.example")))
        {
            other.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await other.Content.ReadAsStringAsync(Token)).ShouldContain("is not the one this invitation was sent to");
        }

        using (var unverified = await AcceptWithProviderAsync(browser, idp, link, FakeIdentity.Of("grace@corp.example") with { EmailVerified = false }))
        {
            (await unverified.Content.ReadAsStringAsync(Token)).ShouldContain("not verified");
        }

        browser.HasCookie(ApplicationCookie).ShouldBeFalse();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.FindByEmailAsync("grace@corp.example")).ShouldBeNull();
            (await users.FindByEmailAsync("mallory@corp.example")).ShouldBeNull();
        }

        // The invitation can still be accepted, and the failures are failed sign-ins.
        (await browser.PageAsync(link.PathAndQuery)).ShouldContain("grace@corp.example");
        (await ActivityAsync(host, "user.login_failed", count: 2)).ShouldAllBe(row => Details(row)["reason"] == LoginFailureReasons.NotAllowed);
    }

    [Fact]
    public async Task AProviderThatLinksExistingUsersLinksOnlyWhenTheTokenSaysTheAddressIsVerified()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider => provider.LinkExistingUsers = true);
        var editor = await CreateUserAsync(host);
        var subject = FakeIdentity.Of(editor.Email);

        foreach (var unverified in new[] { subject, subject with { EmailVerified = false } })
        {
            using var browser = new HostedPagesBrowser(host);
            using var back = await SignInWithProviderAsync(browser, idp, unverified);
            using var page = await browser.FollowAsync(back);
            (await page.Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
            browser.HasCookie(ApplicationCookie).ShouldBeFalse();
        }

        (await LoginsAsync(host, editor.Id)).ShouldBeEmpty();

        using (var browser = new HostedPagesBrowser(host))
        {
            using var back = await SignInWithProviderAsync(browser, idp, subject with { EmailVerified = true });
            (await ExternalLoginAsync(browser, back)).ShouldBe("/");
            browser.HasCookie(ApplicationCookie).ShouldBeTrue();
        }

        (await LinkedUserAsync(host, Corp, subject.Subject)).Id.ShouldBe(editor.Id);
        (await RoleIdsAsync(host, (await LinkedUserAsync(host, Corp, subject.Subject)))).ShouldBe([BuiltInRoles.Editor]);

        // A second account at the provider with the same address does not join the first.
        using (var browser = new HostedPagesBrowser(host))
        {
            using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of(editor.Email) with { EmailVerified = true });
            (await (await browser.FollowAsync(back)).Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
        }

        (await LoginsAsync(host, editor.Id)).ShouldHaveSingleItem().ProviderKey.ShouldBe(subject.Subject);
        var linked = (await ActivityAsync(host, "user.login_linked", count: 1)).ShouldHaveSingleItem();
        linked.TargetId.ShouldBe(editor.Id.ToString("D"));
        Details(linked)["provider"].ShouldBe(Corp);
        (await ActivityAsync(host, "user.login_failed", count: 3)).ShouldAllBe(row => Details(row)["reason"] == LoginFailureReasons.NotAllowed);
    }

    // Linking does not need auto-provisioning, and a user who cannot sign in now is not linked.
    [Fact]
    public async Task WithoutAutoProvisioningAProviderStillLinksAnExistingUserButNotADeactivatedOne()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider =>
        {
            provider.AutoProvision = false;
            provider.LinkExistingUsers = true;
        });
        var active = await CreateUserAsync(host);
        var gone = await CreateUserAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.DeactivateAsync((await users.FindByIdAsync(gone.Id.ToString()))!)).Succeeded.ShouldBeTrue();
        }

        using (var browser = new HostedPagesBrowser(host))
        {
            using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of(active.Email) with { EmailVerified = true });
            (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        }

        foreach (var refused in new[] { gone.Email, "nobody@corp.example" })
        {
            using var browser = new HostedPagesBrowser(host);
            using var back = await SignInWithProviderAsync(browser, idp, FakeIdentity.Of(refused) with { EmailVerified = true });
            (await (await browser.FollowAsync(back)).Content.ReadAsStringAsync(Token)).ShouldContain("is not linked to a user here");
        }

        (await LoginsAsync(host, active.Id)).ShouldHaveSingleItem().LoginProvider.ShouldBe(Corp);
        (await LoginsAsync(host, gone.Id)).ShouldBeEmpty();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<CmsUserManager>().FindByEmailAsync("nobody@corp.example")).ShouldBeNull();
        }
    }

    // The provider's amr counts as before for a user linked at its first sign-in, and the mapped roles follow its groups.
    [Fact]
    public async Task ALinkedExistingUserTakesTheProvidersMfaOnlyFromAProviderTrustedForIt()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider =>
        {
            provider.LinkExistingUsers = true;
            provider.RoleMappings.Add(new SsoRoleMapping { Group = "guarded", Role = "Guarded" });
        });
        using (var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)))
        {
            var created = await AdminFixture.SendAsync(admin, HttpMethod.Post, "roles", """{ "name": "Guarded", "requireTwoFactor": true, "grants": [ { "permission": "schema.read" } ] }""");
            created.Status.ShouldBe(HttpStatusCode.Created, created.Body?.ToJsonString());
        }

        var una = await CreateUserAsync(host);
        var tim = await CreateUserAsync(host);

        var untrusted = new FakeIdentity("sub-una", una.Email, ["guarded"], ["pwd", "mfa"]) { EmailVerified = true };
        (await AuthorizeThroughProviderAsync(host, idp, Corp, untrusted)).ShouldStartWith("/account/enable-two-factor?");

        var request = AuthorizeRequest.For(FluentCmsTestHost.OAuthClientId, FluentCmsTestHost.OAuthRedirectUri);
        using var browser = new HostedPagesBrowser(host);
        using var back = await SignInWithProviderAsync(browser, idp, new FakeIdentity("sub-tim", tim.Email, ["guarded"], ["pwd", "mfa"]) { EmailVerified = true }, request.Url, provider: Trusted);
        var tokens = await request.ExchangeAsync(host, await browser.FollowAsync(back));
        Amr(tokens.GetProperty("access_token").GetString()!).ShouldContain("mfa");
        (await RoleIdsAsync(host, await LinkedUserAsync(host, Trusted, "sub-tim"))).Count.ShouldBe(2);
    }

    [Fact]
    public async Task ExternalLoginsAreListedAndRemovedThroughTheUsersRoutesWithActivityRows()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, provider => provider.LinkExistingUsers = true);

        // Created through one provider and linked through the other, with no password.
        var lin = FakeIdentity.Of("lin@corp.example") with { EmailVerified = true };
        foreach (var provider in new[] { Corp, Trusted })
        {
            using var browser = new HostedPagesBrowser(host);
            using var back = await SignInWithProviderAsync(browser, idp, lin, provider: provider);
            (await ExternalLoginAsync(browser, back)).ShouldBe("/");
        }

        var user = await LinkedUserAsync(host, Corp, lin.Subject);
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var listed = await AdminFixture.SendAsync(admin, HttpMethod.Get, $"users/{user.Id}/logins");
        listed.Status.ShouldBe(HttpStatusCode.OK, listed.Body?.ToJsonString());
        var items = listed.Json["items"]!.AsArray();
        items.Select(item => item!["provider"]!.GetValue<string>()).ShouldBe([Corp, Trusted]);
        items.ShouldAllBe(item => item!["providerKey"]!.GetValue<string>() == lin.Subject);
        items[0]!["displayName"]!.GetValue<string>().ShouldBe("Corp");

        var stamp = user.SecurityStamp;
        (await AdminFixture.SendAsync(admin, HttpMethod.Delete, $"users/{user.Id}/logins/{Trusted}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await LinkedUserAsync(host, Corp, lin.Subject)).SecurityStamp.ShouldNotBe(stamp);

        var last = await AdminFixture.SendAsync(admin, HttpMethod.Delete, $"users/{user.Id}/logins/{Corp}");
        last.Status.ShouldBe(HttpStatusCode.Conflict, last.Body?.ToJsonString());
        last.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastWayIn);

        var missing = await AdminFixture.SendAsync(admin, HttpMethod.Delete, $"users/{user.Id}/logins/{Trusted}");
        missing.Status.ShouldBe(HttpStatusCode.NotFound);
        missing.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LoginNotFound);
        (await LoginsAsync(host, user.Id)).ShouldHaveSingleItem().LoginProvider.ShouldBe(Corp);

        using (var editor = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token)))
        {
            (await AdminFixture.SendAsync(editor, HttpMethod.Get, $"users/{user.Id}/logins")).Status.ShouldBe(HttpStatusCode.Forbidden);
            (await AdminFixture.SendAsync(editor, HttpMethod.Delete, $"users/{user.Id}/logins/{Corp}")).Status.ShouldBe(HttpStatusCode.Forbidden);
        }

        var unlinked = (await ActivityAsync(host, "user.login_unlinked", count: 1)).ShouldHaveSingleItem();
        unlinked.TargetId.ShouldBe(user.Id.ToString("D"));
        Details(unlinked)["provider"].ShouldBe(Trusted);
        (await ActivityAsync(host, "user.login_linked", count: 2)).Select(row => Details(row)["provider"]).ShouldBe([Corp, Trusted], ignoreOrder: true);
    }

    [Fact]
    public async Task AUserRemovesItsOwnLoginsThroughMeButNotItsLastWayToSignIn()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        var me = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await AddLoginAsync(host, me.Id, Corp, "sub-me");
        await AddLoginAsync(host, me.Id, "gone", "sub-old");
        using var client = await host.AsUserAsync(me);

        (await AdminFixture.SendAsync(client, HttpMethod.Get, "me/logins")).Json["items"]!.AsArray().Select(item => item!["provider"]!.GetValue<string>()).ShouldBe([Corp, "gone"]);

        // The login of a provider that is not configured is no way in: it does not save the configured one, and it goes.
        var last = await AdminFixture.SendAsync(client, HttpMethod.Delete, $"me/logins/{Corp}");
        last.Status.ShouldBe(HttpStatusCode.Conflict);
        last.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastWayIn);
        (await AdminFixture.SendAsync(client, HttpMethod.Delete, "me/logins/gone")).Status.ShouldBe(HttpStatusCode.NoContent);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
            (await users.AddPasswordAsync((await users.FindByIdAsync(me.Id.ToString()))!, Password)).Succeeded.ShouldBeTrue();
        }

        (await AdminFixture.SendAsync(client, HttpMethod.Delete, $"me/logins/{Corp}")).Status.ShouldBe(HttpStatusCode.NoContent);
        (await LoginsAsync(host, me.Id)).ShouldBeEmpty();
        (await ActivityAsync(host, "user.login_unlinked", count: 2)).ShouldAllBe(row => row.TargetId == me.Id.ToString("D"));

        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var keyClient = host.AsKey(key);
        (await AdminFixture.SendAsync(keyClient, HttpMethod.Get, "me/logins")).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WithLocalLoginDisabledAPasswordIsNoWayToSignIn()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp, disableLocalLogin: true);
        var editor = await CreateUserAsync(host);
        await AddLoginAsync(host, editor.Id, Corp, "sub-editor");
        using var admin = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));

        var refused = await AdminFixture.SendAsync(admin, HttpMethod.Delete, $"users/{editor.Id}/logins/{Corp}");

        refused.Status.ShouldBe(HttpStatusCode.Conflict);
        refused.Json["code"]!.GetValue<string>().ShouldBe(IdentityErrorCodes.LastWayIn);
    }

    // With a public origin the provider is given, and the code is redeemed with, the callback on that origin (#2821), so a
    // proxy that does not forward the host does not send the internal address to the provider. A host's own handler of the
    // redirect keeps it.
    [Fact]
    public async Task WithAPublicOriginTheProviderCallbackIsBuiltFromIt()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp)
            .WithServices(services => services.Configure<OpenIdConnectOptions>(Corp, options => options.Events.OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.Prompt = "login";
                return Task.CompletedTask;
            }))
            .Configure(cms =>
            {
                cms.ReverseProxy.PathBase = "/cms";
                cms.PublicOrigin = "https://cms.example.com";
            });
        using var browser = new HostedPagesBrowser(host);

        var login = await browser.PageAsync("/cms/account/login");
        using var challenge = await browser.PostAsync("/cms/account/login?handler=External", login, new Dictionary<string, string> { ["provider"] = Corp, ["ReturnUrl"] = "" });
        var authorize = challenge.Headers.Location!;
        QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString().ShouldBe($"https://cms.example.com/cms/account/sso/{Corp}");
        QueryHelpers.ParseQuery(authorize.Query)["prompt"].ToString().ShouldBe("login");

        using var back = await browser.PostAsync($"/cms/account/sso/{Corp}", idp.Authorize(authorize, FakeIdentity.Of("pat@corp.example")));
        idp.TokenRequests.ShouldBe(1);
        back.Headers.Location!.OriginalString.ShouldStartWith("/cms/account/external-login?");
        using var page = await browser.GetAsync(back.Headers.Location.OriginalString);
        page.Headers.Location!.OriginalString.ShouldBe("/cms/");
    }

    // A cancel at the provider during an invitation goes back to the invitation, which can still be accepted.
    [Fact]
    public async Task AProviderErrorDuringAnInvitationGoesBackToTheInvitation()
    {
        using var idp = new FakeOidcProvider();
        await using var host = SsoHost(idp);
        var link = await InviteAsync(host, "kim@corp.example");
        using var browser = new HostedPagesBrowser(host);

        using var back = await AcceptWithProviderAsync(browser, idp, link, FakeIdentity.Of("kim@corp.example"), error: "access_denied");

        back.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await back.Content.ReadAsStringAsync(Token);
        page.ShouldContain("did not complete");
        page.ShouldContain("kim@corp.example");
        idp.TokenRequests.ShouldBe(0);
    }

    private async Task<Uri> InviteAsync(FluentCmsTestHost host, string address)
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
            await scope.ServiceProvider.GetRequiredService<InvitationService>().InviteAsync(address, [BuiltInRoles.Editor], new Uri("https://localhost/account/accept-invitation"), Token);
        }

        return _emails.LinkTo(address);
    }

    // The invitation page's button for the provider, the provider, and the callback, which returns to the invitation page;
    // returns that page's answer.
    private static async Task<HttpResponseMessage> AcceptWithProviderAsync(HostedPagesBrowser browser, FakeOidcProvider idp, Uri link, FakeIdentity user, string? error = null)
    {
        var page = await browser.PageAsync(link.PathAndQuery);
        var token = QueryHelpers.ParseQuery(link.Query)["token"].ToString();
        using var challenge = await browser.PostAsync("/account/accept-invitation?handler=External", page, new Dictionary<string, string> { ["provider"] = Corp, ["Token"] = token });
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync(Token));
        var authorize = challenge.Headers.Location!;

        var callback = new Uri(QueryHelpers.ParseQuery(authorize.Query)["redirect_uri"].ToString());
        using var back = await browser.PostAsync(callback.PathAndQuery, idp.Authorize(authorize, user, error));
        back.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        back.Headers.Location!.OriginalString.ShouldStartWith("/account/accept-invitation?");
        return await browser.GetAsync(back.Headers.Location.OriginalString);
    }

    private static async Task AddLoginAsync(FluentCmsTestHost host, Guid userId, string provider, string key)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        (await users.AddLoginAsync((await users.FindByIdAsync(userId.ToString()))!, new UserLoginInfo(provider, key, provider))).Succeeded.ShouldBeTrue();
    }

    private static async Task<IList<UserLoginInfo>> LoginsAsync(FluentCmsTestHost host, Guid userId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        return await users.GetLoginsAsync((await users.FindByIdAsync(userId.ToString()))!);
    }
}
