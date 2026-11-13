using System.Net;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Diagnostics;
using FluentCMS.Core.IntegrationTests.Identity;
using FluentCMS.Events;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// A member's tokens: what they carry, what they read on the Delivery API, what refuses them, the direct login grant, and
// what ends them.
public sealed class MemberTokenTests(MemberFixture fixture) : IClassFixture<MemberFixture>
{
    private const string Password = MemberFixture.Password;

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMembersTokenCarriesTheMembersAudienceTheKindMemberAndOnlyAMembersScopes()
    {
        var member = await fixture.Host.CreateMemberAsync(cancellationToken: Token);

        var tokens = await fixture.Host.SignInAsync(member, "openid", "profile", "email", "offline_access");
        var access = Payload(tokens.AccessToken);
        Audiences(access).ShouldBe(["fluentcms:members"]);
        access.GetProperty("fcms_kind").GetString().ShouldBe("Member");
        access.GetProperty("sub").GetString().ShouldBe(member.Id.ToString("D"));
        access.GetProperty("scope").GetString()!.Split(' ').ShouldBe(["openid", "profile", "email", "offline_access"], ignoreOrder: true);
        access.TryGetProperty("roles", out _).ShouldBeFalse();
        access.TryGetProperty(Principal.GroupsClaim, out _).ShouldBeFalse();
        access.TryGetProperty("fcms_security_stamp", out _).ShouldBeFalse();

        var identity = Payload(tokens.IdentityToken!);
        identity.GetProperty("email").GetString().ShouldBe(member.Email);

        // The scopes of the other surfaces are not the member client's to ask for, so no token is issued with them.
        foreach (var scope in (string[])[TokenScopes.Management, TokenScopes.Draft, TokenScopes.Mcp])
        {
            await Should.ThrowAsync<InvalidOperationException>(() => fixture.Host.SignInAsync(member, "openid", scope));
        }

        // A refresh keeps it a member's token.
        var refreshed = await fixture.RefreshAsync(tokens.RefreshToken!, FluentCmsTestHost.OAuthMemberClientId);
        refreshed.Status.ShouldBe(HttpStatusCode.OK, refreshed.Body?.ToJsonString());
        var again = Payload(refreshed.Json["access_token"]!.GetValue<string>());
        Audiences(again).ShouldBe(["fluentcms:members"]);
        again.GetProperty("fcms_kind").GetString().ShouldBe("Member");

        using var client = fixture.Bearer(tokens.AccessToken);
        var info = await MemberFixture.SendAsync(client, HttpMethod.Get, "/connect/userinfo");
        info.Status.ShouldBe(HttpStatusCode.OK, info.Body?.ToJsonString());
        info.Json["sub"]!.GetValue<string>().ShouldBe(member.Id.ToString("D"));
        info.Json["email"]!.GetValue<string>().ShouldBe(member.Email);
        info.Json["email_verified"]!.GetValue<bool>().ShouldBeTrue();

        // An admin user's token is as it was.
        var admin = await fixture.Host.SignInAsync(await fixture.Host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        Audiences(Payload(admin.AccessToken)).ShouldBe(["fluentcms:management"]);
        Payload(admin.AccessToken).GetProperty("fcms_kind").GetString().ShouldBe("AdminUser");
    }

    [Fact]
    public async Task AMemberReadsWhatThePublicRoleTheMemberRoleAndTheRolesOfItsGroupsAllow()
    {
        using var visitor = fixture.Host.CreateSecureClient();
        (await MemberFixture.TitlesAsync(visitor, "bulletin")).Titles.ShouldBe(["Open to all"]);
        // A type a caller may not read is not there for it.
        (await MemberFixture.TitlesAsync(visitor, "perk")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await MemberFixture.TitlesAsync(visitor, "dossier")).Status.ShouldBe(HttpStatusCode.NotFound);

        using var plain = await fixture.Host.AsMemberAsync(await fixture.Host.CreateMemberAsync(cancellationToken: Token));
        (await MemberFixture.TitlesAsync(plain, "bulletin")).Titles.ShouldBe(["Open to all"]);
        (await MemberFixture.TitlesAsync(plain, "perk")).Titles.ShouldBe(["Standard perk"]);
        (await MemberFixture.TitlesAsync(plain, "dossier")).Status.ShouldBe(HttpStatusCode.NotFound);

        using var insider = await fixture.Host.AsMemberAsync(await fixture.Host.CreateMemberAsync(groups: ["insiders"], cancellationToken: Token));
        (await MemberFixture.TitlesAsync(insider, "bulletin")).Titles.ShouldBe(["Open to all"]);
        (await MemberFixture.TitlesAsync(insider, "perk")).Titles.ShouldBe(["Standard perk"]);
        (await MemberFixture.TitlesAsync(insider, "dossier")).Titles.ShouldBe(["For insiders"]);

        // Published content only: a member's token has no draft scope.
        var drafts = await MemberFixture.SendAsync(insider, HttpMethod.Get, "/api/delivery/v1/dossier?status=draft");
        drafts.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARowRuleThatReadsTheGroupsGivesMembersOfDifferentGroupsDifferentEntriesAndAChangeHoldsFromTheNextRequest()
    {
        var gold = await fixture.Host.CreateMemberAsync(groups: ["gold"], cancellationToken: Token);
        var standard = await fixture.Host.CreateMemberAsync(cancellationToken: Token);
        using var goldClient = await fixture.Host.AsMemberAsync(gold);
        using var standardClient = await fixture.Host.AsMemberAsync(standard);

        (await MemberFixture.TitlesAsync(goldClient, "perk")).Titles.ShouldBe(["Gold perk"]);
        (await MemberFixture.TitlesAsync(standardClient, "perk")).Titles.ShouldBe(["Standard perk"]);

        // The same token, the next request: the group is read again, not taken from the token.
        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{gold.Id:D}/groups/{fixture.Gold:D}")).Status.ShouldBe(HttpStatusCode.OK);
        (await MemberFixture.TitlesAsync(goldClient, "perk")).Titles.ShouldBe(["Standard perk"]);

        (await fixture.ManageAsync(HttpMethod.Put, $"members/{standard.Id:D}/groups/{fixture.Gold:D}")).Status.ShouldBe(HttpStatusCode.OK);
        (await MemberFixture.TitlesAsync(standardClient, "perk")).Titles.ShouldBe(["Gold perk"]);

        (await MemberFixture.TitlesAsync(standardClient, "dossier")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{standard.Id:D}/groups/{fixture.Insiders:D}")).Status.ShouldBe(HttpStatusCode.OK);
        (await MemberFixture.TitlesAsync(standardClient, "dossier")).Titles.ShouldBe(["For insiders"]);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: standardClient)).Json["groups"]!.AsArray().Select(group => group!.GetValue<string>()).ShouldBe(["gold", "insiders"]);
    }

    [Fact]
    public async Task TheOutputCacheNeverServesOneCallersReadToAnother()
    {
        var gold = await fixture.Host.CreateMemberAsync(groups: ["gold"], cancellationToken: Token);
        using var goldClient = await fixture.Host.AsMemberAsync(gold);
        using var standardClient = await fixture.Host.AsMemberAsync(await fixture.Host.CreateMemberAsync(cancellationToken: Token));
        using var visitor = fixture.Host.CreateSecureClient();

        // Each reads the same URL twice, in turn: a copy kept for one of them would show up at another.
        for (var round = 0; round < 2; round++)
        {
            (await MemberFixture.TitlesAsync(goldClient, "perk")).Titles.ShouldBe(["Gold perk"]);
            (await MemberFixture.TitlesAsync(standardClient, "perk")).Titles.ShouldBe(["Standard perk"]);
            (await MemberFixture.TitlesAsync(visitor, "perk")).Status.ShouldBe(HttpStatusCode.NotFound);
            (await MemberFixture.TitlesAsync(visitor, "bulletin")).Titles.ShouldBe(["Open to all"]);
            (await MemberFixture.TitlesAsync(goldClient, "bulletin")).Titles.ShouldBe(["Open to all"]);
        }

        // A member's answer is its own: no shared cache may keep it.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/delivery/v1/perk");
        using var response = await goldClient.SendAsync(request, Token);
        response.Headers.CacheControl!.Public.ShouldBeFalse();
    }

    [Fact]
    public async Task AMembersTokenIsRefusedByEveryManagementRoute()
    {
        var member = await fixture.Host.CreateMemberAsync(groups: ["insiders"], cancellationToken: Token);
        using var client = await fixture.Host.AsMemberAsync(member);

        foreach (var path in (string[])["me", "me/sites", "members", "member-groups", "users", "roles", "sites", "content/bulletin", "schema", "system/diagnostics", "activity"])
        {
            var refused = await fixture.ManageAsync(HttpMethod.Get, path, client: client);
            refused.Status.ShouldBe(HttpStatusCode.Forbidden, path);
            refused.Json["code"]!.GetValue<string>().ShouldBe("FCMS3103");
        }

        (await fixture.ManageAsync(HttpMethod.Post, "content/bulletin", """{ "title": "By a member" }""", client)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{member.Id:D}/groups/{fixture.Gold:D}", client: client)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.ManageAsync(HttpMethod.Post, "member-groups", $$"""{ "name": "mine", "roleId": "{{BuiltInRoles.SuperAdmin:D}}" }""", client)).Status.ShouldBe(HttpStatusCode.Forbidden);

        // Even with a group bound to Super Admin, the token manages nothing: its scopes leave it reading published content.
        var role = $"all-{Guid.NewGuid():N}";
        await fixture.Host.CreateRoleAsync(role, ["*"], Token);
        var groupName = $"all-{Guid.NewGuid():N}";
        await fixture.Host.CreateMemberGroupAsync(groupName, role, Token);
        using var powerful = await fixture.Host.AsMemberAsync(await fixture.Host.CreateMemberAsync(groups: [groupName], cancellationToken: Token));
        (await fixture.ManageAsync(HttpMethod.Get, "members", client: powerful)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.ManageAsync(HttpMethod.Post, "content/bulletin", """{ "title": "By a member" }""", powerful)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await MemberFixture.TitlesAsync(powerful, "dossier")).Titles.ShouldBe(["For insiders"]);
        (await MemberFixture.SendAsync(powerful, HttpMethod.Get, "/api/delivery/v1/dossier?status=draft")).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TheClientNamesThePoolSoNeitherPoolsSignInGetsTheOthersTokens()
    {
        var address = await fixture.RegisterVerifiedAsync();
        var admin = await fixture.Host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);

        // An admin session asked for by the member client: the member login page, and no code.
        using var api = fixture.Host.CreateSecureClient();
        using (var request = new HttpRequestMessage(HttpMethod.Get, Authorize(FluentCmsTestHost.OAuthMemberClientId, "openid")))
        {
            request.Headers.Add("Cookie", await fixture.Host.SignInCookieAsync(admin));
            using var response = await api.SendAsync(request, Token);
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            response.Headers.Location!.AbsolutePath.ShouldBe("/account/members/login");
        }

        // A member session asked for by the admin client: the admin login page, and no code.
        using var browser = new HostedPagesBrowser(fixture.Host, "10.9.0.2");
        using (var signedIn = await browser.SubmitAsync("/account/members/login", Credentials(address, Password)))
        {
            signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        using (var response = await browser.GetAsync(Authorize(FluentCmsTestHost.OAuthClientId, "openid management")))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            response.Headers.Location!.AbsolutePath.ShouldBe("/account/login");
        }

        // A member's refresh token is not redeemed through the admin client, nor an admin user's through the member client.
        var member = new TestMember(await fixture.MemberIdAsync(address), address, Principal.Anonymous);
        var memberTokens = await fixture.Host.SignInAsync(member);
        var adminTokens = await fixture.Host.SignInAsync(admin);
        (await fixture.RefreshAsync(memberTokens.RefreshToken!, FluentCmsTestHost.OAuthClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.RefreshAsync(adminTokens.RefreshToken!, FluentCmsTestHost.OAuthMemberClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);

        // The direct login is for the member pool: the admin client is not let near it, and an admin user is no member.
        var wrongClient = await fixture.DirectLoginAsync(address, Password, FluentCmsTestHost.OAuthClientId);
        wrongClient.Status.ShouldBe(HttpStatusCode.BadRequest);
        wrongClient.Json["access_token"].ShouldBeNull();
        (await fixture.DirectLoginAsync(admin.Email, Password)).Json["error"]!.GetValue<string>().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task TheDirectLoginGivesAVerifiedMemberTokensAndAnswersEveryRefusalAlike()
    {
        var address = await fixture.RegisterVerifiedAsync();

        var tokens = await fixture.DirectLoginAsync(address, Password, scope: "openid profile email offline_access");
        tokens.Status.ShouldBe(HttpStatusCode.OK, tokens.Body?.ToJsonString());
        tokens.Json["refresh_token"]!.GetValue<string>().ShouldNotBeNullOrEmpty();
        var access = Payload(tokens.Json["access_token"]!.GetValue<string>());
        Audiences(access).ShouldBe(["fluentcms:members"]);
        access.GetProperty("fcms_kind").GetString().ShouldBe("Member");
        access.GetProperty("amr").GetString().ShouldBe("pwd");
        using (var client = fixture.Bearer(tokens.Json["access_token"]!.GetValue<string>()))
        {
            (await MemberFixture.TitlesAsync(client, "perk")).Titles.ShouldBe(["Standard perk"]);
            (await fixture.ManageAsync(HttpMethod.Get, "me", client: client)).Status.ShouldBe(HttpStatusCode.Forbidden);
        }

        // The grant asks for no scope a member's token may not hold.
        var widened = await fixture.DirectLoginAsync(address, Password, scope: "openid management");
        widened.Status.ShouldBe(HttpStatusCode.BadRequest);
        widened.Json["access_token"].ShouldBeNull();

        var unverified = MemberFixture.NewAddress();
        (await fixture.MembersAsync(HttpMethod.Post, "register", $$"""{ "email": "{{unverified}}", "password": "{{Password}}" }""")).Status.ShouldBe(HttpStatusCode.Accepted);
        var deactivated = await fixture.RegisterVerifiedAsync();
        (await fixture.ManageAsync(HttpMethod.Post, $"members/{await fixture.MemberIdAsync(deactivated):D}/deactivate")).Status.ShouldBe(HttpStatusCode.OK);

        var refusals = new[]
        {
            await fixture.DirectLoginAsync(address, "Not-The-Password-1"),
            await fixture.DirectLoginAsync(MemberFixture.NewAddress(), Password),
            await fixture.DirectLoginAsync(unverified, Password),
            await fixture.DirectLoginAsync(deactivated, Password),
            await fixture.DirectLoginAsync(address, Password, FluentCmsTestHost.OAuthMemberClientId),
            await fixture.DirectLoginAsync(address, ""),
        };
        foreach (var refusal in refusals)
        {
            refusal.Status.ShouldBe(HttpStatusCode.BadRequest);
            refusal.Json.ToJsonString().ShouldBe(refusals[0].Json.ToJsonString());
        }

        refusals[0].Json["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        // Neither the password nor the address of a refusal is in the log.
        fixture.Logs.Entries.ShouldContain(entry => entry.EventId.Id == MemberLog.DirectLoginRefusedId);
        fixture.Logs.Entries.ShouldNotContain(entry => entry.Message.Contains(Password, StringComparison.Ordinal) || entry.Message.Contains("Not-The-Password-1", StringComparison.Ordinal));
        fixture.Logs.Entries.Where(entry => entry.EventId.Id == MemberLog.DirectLoginRefusedId).ShouldAllBe(entry => !entry.Message.Contains(unverified, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWrongPasswordAtTheDirectLoginCountsTowardsTheLockout()
    {
        var address = await fixture.RegisterVerifiedAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await fixture.DirectLoginAsync(address, $"Wrong-Password-Number-{attempt}")).Json["error"]!.GetValue<string>().ShouldBe("invalid_grant");
        }

        // Locked out: the right password gets the answer a wrong one gets, here and on the hosted page.
        var locked = await fixture.DirectLoginAsync(address, Password);
        locked.Status.ShouldBe(HttpStatusCode.BadRequest);
        locked.Json["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        using var browser = new HostedPagesBrowser(fixture.Host, "10.9.0.3");
        using (var refused = await browser.SubmitAsync("/account/members/login", Credentials(address, Password)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("Sign-in failed.");
        }

        var failures = await fixture.ActivityAsync("member.login_failed", (await fixture.MemberIdAsync(address)).ToString("D"));
        // The attempt that locks the member out, and the two right passwords that came after it.
        failures.Count(row => row.Details!.Contains(LoginFailureReasons.LockedOut, StringComparison.Ordinal)).ShouldBe(3);
        failures.Count(row => row.Details!.Contains(LoginFailureReasons.InvalidCredentials, StringComparison.Ordinal)).ShouldBe(4);
    }

    // On a host of its own, since it moves the clock past the lockout. The lockout is Identity's rule on the host's clock:
    // still on at its end, and over a tick later.
    [Fact]
    public async Task ALockedOutMemberIsRefusedUntilTheLockoutEndsAndSignsInAfterItOnTheGrantAndThePage()
    {
        await using var host = MemberFixture.NewHost(new HostedPagesTests.CapturedEmails());
        var member = await host.CreateMemberAsync(password: Password, cancellationToken: Token);
        using var browser = new HostedPagesBrowser(host, "10.9.0.4");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await MemberFixture.DirectLoginAsync(host, member.Email, $"Wrong-Password-Number-{attempt}")).Status.ShouldBe(HttpStatusCode.BadRequest);
        }

        host.Time.Advance(new MemberOptions().Lockout.DefaultLockoutTimeSpan);
        var locked = await MemberFixture.DirectLoginAsync(host, member.Email, Password);
        locked.Status.ShouldBe(HttpStatusCode.BadRequest);
        locked.Json["error"]!.GetValue<string>().ShouldBe("invalid_grant");
        using (var refused = await browser.SubmitAsync("/account/members/login", Credentials(member.Email, Password)))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await refused.Content.ReadAsStringAsync(Token)).ShouldContain("Sign-in failed.");
        }

        host.Time.Advance(TimeSpan.FromTicks(1));
        (await MemberFixture.DirectLoginAsync(host, member.Email, Password)).Status.ShouldBe(HttpStatusCode.OK);
        using var signedIn = await browser.SubmitAsync("/account/members/login", Credentials(member.Email, Password));
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task TheDirectLoginIsRefusedAndNotAdvertisedWhileTheOptionIsOff()
    {
        using (var client = fixture.Host.CreateSecureClient())
        {
            var on = await MemberFixture.SendAsync(client, HttpMethod.Get, "/.well-known/openid-configuration");
            on.Json["grant_types_supported"]!.AsArray().Select(grant => grant!.GetValue<string>()).ShouldContain("member_password");
        }

        var emails = new HostedPagesTests.CapturedEmails();
        await using var host = MemberFixture.NewHost(emails, configure: cms => cms.Members.AllowDirectLogin = false);
        var member = await host.CreateMemberAsync(password: Password, cancellationToken: Token);

        using var api = host.CreateSecureClient();
        var off = await MemberFixture.SendAsync(api, HttpMethod.Get, "/.well-known/openid-configuration");
        off.Json["grant_types_supported"]!.AsArray().Select(grant => grant!.GetValue<string>()).ShouldNotContain("member_password");
        off.Json["grant_types_supported"]!.AsArray().Select(grant => grant!.GetValue<string>()).ShouldContain("authorization_code");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "member_password",
            ["client_id"] = MemberFixture.DirectClientId,
            ["username"] = member.Email,
            ["password"] = Password,
        });
        using var response = await api.PostAsync("/connect/token", form, Token);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        body.RootElement.GetProperty("error").GetString().ShouldBe("invalid_grant");

        // The hosted pages still sign the member in.
        (await host.SignInAsync(member)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ADeactivatedMemberIsRefusedAtTheTokenEndpointAndItsAccessTokensStopAtOnce()
    {
        var address = await fixture.RegisterVerifiedAsync();
        var id = await fixture.MemberIdAsync(address);
        var hosted = await fixture.Host.SignInAsync(new TestMember(id, address, Principal.Anonymous));
        var direct = await fixture.DirectLoginAsync(address, Password, scope: "openid");
        using var hostedClient = fixture.Bearer(hosted.AccessToken);
        using var directClient = fixture.Bearer(direct.Json["access_token"]!.GetValue<string>());
        (await MemberFixture.TitlesAsync(hostedClient, "perk")).Status.ShouldBe(HttpStatusCode.OK);
        (await MemberFixture.TitlesAsync(directClient, "perk")).Status.ShouldBe(HttpStatusCode.OK);

        var deactivated = await fixture.ManageAsync(HttpMethod.Post, $"members/{id:D}/deactivate");
        deactivated.Status.ShouldBe(HttpStatusCode.OK, deactivated.Body?.ToJsonString());
        deactivated.Json["deactivatedAt"].ShouldNotBeNull();

        // Both access tokens, the one with a session and the one of a direct login without, stop on the next request.
        (await MemberFixture.TitlesAsync(hostedClient, "perk")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await MemberFixture.TitlesAsync(directClient, "perk")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.MembersAsync(HttpMethod.Get, "me", client: hostedClient)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(hosted.RefreshToken!, FluentCmsTestHost.OAuthMemberClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Host.SignInAsync(new TestMember(id, address, Principal.Anonymous)));

        (await fixture.ManageAsync(HttpMethod.Post, $"members/{id:D}/reactivate")).Json["deactivatedAt"].ShouldBeNull();
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADeletedMemberIsRefusedAtTheTokenEndpointAndItsAccessTokensStopAtOnce()
    {
        var address = await fixture.RegisterVerifiedAsync();
        var id = await fixture.MemberIdAsync(address);
        (await fixture.ManageAsync(HttpMethod.Put, $"members/{id:D}/groups/{fixture.Insiders:D}")).Status.ShouldBe(HttpStatusCode.OK);
        var hosted = await fixture.Host.SignInAsync(new TestMember(id, address, Principal.Anonymous));
        var direct = await fixture.DirectLoginAsync(address, Password, scope: "openid");
        using var hostedClient = fixture.Bearer(hosted.AccessToken);
        using var directClient = fixture.Bearer(direct.Json["access_token"]!.GetValue<string>());
        (await MemberFixture.TitlesAsync(hostedClient, "dossier")).Status.ShouldBe(HttpStatusCode.OK);

        (await fixture.ManageAsync(HttpMethod.Delete, $"members/{id:D}")).Status.ShouldBe(HttpStatusCode.NoContent);

        (await MemberFixture.TitlesAsync(hostedClient, "dossier")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await MemberFixture.TitlesAsync(directClient, "bulletin")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await fixture.RefreshAsync(hosted.RefreshToken!, FluentCmsTestHost.OAuthMemberClientId)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.DirectLoginAsync(address, Password)).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ManageAsync(HttpMethod.Get, $"members/{id:D}")).Json["code"]!.GetValue<string>().ShouldBe("FCMS3161");

        // The activity rows keep the id of the member that is gone.
        var rows = await fixture.ActivityAsync("member.", id.ToString("D"));
        rows.Select(row => row.Action).ShouldContain("member.deleted");
        rows.Select(row => row.Action).ShouldContain("member.registered");
        rows.Single(row => row.Action == "member.deleted").ActorType.ShouldBe(PrincipalKind.AdminUser);
    }

    [Fact]
    public async Task EvenATokenThatWasNotRevokedReadsNothingOnceItsMemberIsDeactivated()
    {
        // The evaluator reads the member on every request, so a deactivated one holds nothing whatever became of its tokens.
        var member = await fixture.Host.CreateMemberAsync(groups: ["insiders"], cancellationToken: Token);
        (await fixture.Host.As(member).Content<Dossier>().QueryAsync(new ContentQuery(), Token)).Items.Count.ShouldBe(1);
        (await fixture.Host.As(member).Content<Bulletin>().QueryAsync(new ContentQuery(), Token)).Items.Count.ShouldBe(1);

        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<MemberManager>();
            (await members.DeactivateAsync((await members.FindByIdAsync(member.Id.ToString("D")))!)).Succeeded.ShouldBeTrue();
        }

        // Not even what a visitor reads: to a caller with no grant the type is not there.
        await Should.ThrowAsync<ValidationException>(async () => await fixture.Host.As(member).Content<Dossier>().QueryAsync(new ContentQuery(), Token));
        await Should.ThrowAsync<ValidationException>(async () => await fixture.Host.As(member).Content<Bulletin>().QueryAsync(new ContentQuery(), Token));
    }

    private static string Authorize(string clientId, string scope) =>
        "/connect/authorize" + Microsoft.AspNetCore.Http.QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = FluentCmsTestHost.OAuthRedirectUri,
            ["scope"] = scope,
            ["code_challenge"] = System.Buffers.Text.Base64Url.EncodeToString(System.Security.Cryptography.SHA256.HashData("verifier-verifier-verifier-verifier-verifier"u8)),
            ["code_challenge_method"] = "S256",
            ["state"] = "test",
        });

    internal static Dictionary<string, string> Credentials(string email, string password) => new()
    {
        ["Input.Email"] = email,
        ["Input.Password"] = password,
    };

    internal static JsonElement Payload(string jwt)
    {
        using var document = JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }

    internal static string[] Audiences(JsonElement payload) =>
        payload.GetProperty("aud") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(item => item.GetString()!)]
            : [payload.GetProperty("aud").GetString()!];
}
