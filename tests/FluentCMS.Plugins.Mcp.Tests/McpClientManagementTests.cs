using System.Net;
using System.Net.Http.Headers;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Sites;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpClientTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// /api/management/v1/mcp/clients: the list, approve, reject and revoke, who may call them, what each change records,
// and what bounds the clients nobody answers.
public sealed class McpClientManagementTests(McpClientFixture fixture) : IClassFixture<McpClientFixture>
{
    [Fact]
    public async Task TheListShowsStatusRoleAndLastActivityAndRevokingAClientEndsItsSessionsAndTokensAtOnce()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.3.0.1");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var (clientId, id) = await ApprovedAsync(agent, admin, BuiltInRoles.EditorName, "Listed agent");
        var idle = (await FindAsync(admin, clientId)).ShouldNotBeNull();

        host.Time.Advance(TimeSpan.FromMinutes(5));
        var signedInAt = host.Time.GetUtcNow();
        var tokens = await agent.TokensAsync(clientId, user);
        using var http = agent.Bearer(tokens["access_token"]!.GetValue<string>());
        using var working = await PostAsync(http);
        var active = (await FindAsync(admin, clientId)).ShouldNotBeNull();

        idle["status"]!.GetValue<string>().ShouldBe("approved");
        idle["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.EditorName);
        idle["lastActivityAt"].ShouldBeNull();
        working.StatusCode.ShouldBe(HttpStatusCode.OK);
        active["name"]!.GetValue<string>().ShouldBe("Listed agent");
        active["clientId"]!.GetValue<string>().ShouldBe(clientId);
        active["redirectUris"]!.AsArray().ShouldHaveSingleItem()!.GetValue<string>().ShouldBe(McpAgent.RedirectUri);
        // The time of its newest token, which the store keeps to the second.
        active["lastActivityAt"]!.GetValue<DateTimeOffset>().ShouldBe(DateTimeOffset.FromUnixTimeSeconds(signedInAt.ToUnixTimeSeconds()));
        (await SessionsAsync(user.Id)).ShouldContain(session => session.ClientId == clientId);

        using var revoked = await RevokeAsync(admin, id);
        using var cut = await PostAsync(http);
        using var refreshed = await agent.RefreshAsync(clientId, tokens["refresh_token"]!.GetValue<string>());
        var authorized = await agent.AuthorizeAsync(clientId);
        using var again = await RevokeAsync(admin, id);

        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        cut.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refreshed.IsSuccessStatusCode.ShouldBeFalse();
        authorized.Code.ShouldBeNull();
        authorized.StoppedAt.ShouldBe("/account/error");
        (await FindAsync(admin, clientId)).ShouldBeNull();
        (await SessionsAsync(user.Id)).ShouldNotContain(session => session.ClientId == clientId);
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(again)).ShouldBe("FCMS1603");
    }

    // The clients of the configuration, the admin application among them, are not MCP clients: the list leaves them out
    // and no call here changes one.
    [Fact]
    public async Task AClientThatDidNotRegisterItselfIsNeitherListedNorChanged()
    {
        var host = fixture.Host;
        using var admin = await KeyAsync(host);
        var configured = await ApplicationIdAsync(host, FluentCmsTestHost.OAuthClientId);

        using var approved = await ApproveAsync(admin, configured, BuiltInRoles.EditorName);
        using var rejected = await RejectAsync(admin, configured);
        using var roleChanged = await ChangeRoleAsync(admin, configured, BuiltInRoles.EditorName);
        using var revoked = await RevokeAsync(admin, configured);

        (await FindAsync(admin, FluentCmsTestHost.OAuthClientId)).ShouldBeNull();
        approved.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(approved)).ShouldBe("FCMS1603");
        rejected.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        roleChanged.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(roleChanged)).ShouldBe("FCMS1603");
        revoked.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApplicationIdAsync(host, FluentCmsTestHost.OAuthClientId)).ShouldBe(configured);
    }

    [Fact]
    public async Task TheEndpointsNeedMcpManageForTheInstance()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.3.0.2");
        using var admin = await KeyAsync(host);
        var id = await IdOfAsync(admin, await agent.RegisteredAsync());
        var managerRole = "MCP manager " + Guid.NewGuid().ToString("N")[..6];
        await host.CreateRoleAsync(managerRole, [McpPermissions.Manage], Token);
        using var editor = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        using var manager = await host.AsUserAsync(await host.CreateUserAsync(managerRole, cancellationToken: Token));
        using var deliveryKey = await KeyAsync(host, ApiKeyClass.Delivery);
        using var anonymous = host.CreateClient();

        using var listed = await editor.GetAsync(new Uri(ClientsPath, UriKind.Relative), Token);
        using var registered = await RegisterAsync(editor, Registration("Editor's agent", BuiltInRoles.EditorName, McpAgent.RedirectUri));
        using var approved = await ApproveAsync(editor, id, BuiltInRoles.EditorName);
        using var rejected = await RejectAsync(editor, id);
        using var roleChanged = await ChangeRoleAsync(editor, id, BuiltInRoles.EditorName);
        using var revoked = await RevokeAsync(editor, id);
        using var unsigned = await anonymous.GetAsync(new Uri(ClientsPath, UriKind.Relative), Token);
        using var withoutScope = await deliveryKey.GetAsync(new Uri(ClientsPath, UriKind.Relative), Token);

        foreach (var refused in new[] { listed, registered, approved, rejected, roleChanged, revoked })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(refused)).ShouldBe("FCMS3103");
        }

        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withoutScope.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ListAsync(manager)).Single(item => item!["id"]!.GetValue<Guid>() == id)!["status"]!.GetValue<string>().ShouldBe("pending");
        (await ListAsync(manager)).ShouldNotContain(item => item!["name"]!.GetValue<string>() == "Editor's agent");

        // The permission alone is enough, and the role given to a client need not be one the caller holds: it only limits.
        using var byManager = await ApproveAsync(manager, id, BuiltInRoles.EditorName);
        using var widened = await ChangeRoleAsync(manager, id, BuiltInRoles.SuperAdminName);
        var (_, registeredId) = await RegisteredByAdminAsync(manager, BuiltInRoles.SuperAdminName);
        using var removed = await RevokeAsync(manager, id);
        using var removedRegistered = await RevokeAsync(manager, registeredId);
        byManager.StatusCode.ShouldBe(HttpStatusCode.OK);
        widened.StatusCode.ShouldBe(HttpStatusCode.OK);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        removedRegistered.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // The clients belong to no site, so mcp.manage from a grant limited to a site, or from a key bound to a site, neither
    // registers a client nor changes a client's role, even in that site.
    [Fact]
    public async Task ARegistrationAndARoleChangeNeedAGrantWithNoSiteLimit()
    {
        var host = fixture.Host;
        using var admin = await KeyAsync(host);
        var (clientId, id) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName, "Instance agent");
        var siteManager = await McpSiteTesting.CreateRoleAsync(host, (McpPermissions.Manage, new PermissionConstraints { Sites = [new SiteId(DefaultSite.Id)] }));
        using var siteGrant = await McpSiteTesting.KeyInAsync(host, DefaultSite.Id, siteManager);
        using var boundKey = await BoundKeyAsync(admin, DefaultSite.Id);

        foreach (var caller in new[] { siteGrant, boundKey })
        {
            using var registered = await RegisterAsync(caller, Registration("Site agent", BuiltInRoles.EditorName, McpAgent.RedirectUri));
            using var changed = await ChangeRoleAsync(caller, id, BuiltInRoles.AuthorName);

            registered.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(registered)).ShouldBe("FCMS3103");
            changed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(changed)).ShouldBe("FCMS3103");
        }

        var clients = await ListAsync(admin);
        clients.ShouldNotContain(item => item!["name"]!.GetValue<string>() == "Site agent");
        clients.Single(item => item!["clientId"]!.GetValue<string>() == clientId)!["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.EditorName);
    }

    [Fact]
    public async Task EachChangeRaisesItsEventAndWritesAnActivityRow()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.3.0.3");
        var administrator = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var admin = await host.AsUserAsync(administrator);
        var kept = await agent.RegisteredAsync("Kept agent");
        var refused = await agent.RegisteredAsync("Refused agent");
        var keptId = await IdOfAsync(admin, kept);
        var refusedId = await IdOfAsync(admin, refused);

        using var approved = await ApproveAsync(admin, keptId, BuiltInRoles.AuthorName);
        using var roleChanged = await ChangeRoleAsync(admin, keptId, BuiltInRoles.EditorName);
        using var unchanged = await ChangeRoleAsync(admin, keptId, BuiltInRoles.Editor.ToString("D"));
        using var rejected = await RejectAsync(admin, refusedId);
        using var revoked = await RevokeAsync(admin, keptId);
        var (desk, deskId) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName, "Desk agent");
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        roleChanged.StatusCode.ShouldBe(HttpStatusCode.OK);
        unchanged.StatusCode.ShouldBe(HttpStatusCode.OK);
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var ofKept = await RowsAsync(host, administrator, keptId);
        var ofRefused = await RowsAsync(host, administrator, refusedId);
        var ofDesk = await RowsAsync(host, administrator, deskId);

        // Setting the role a client has is no change, so it writes no row.
        ofKept.Select(row => row.Action).ShouldBe(["mcp.client_registered", "mcp.client_approved", "mcp.client_role_changed", "mcp.client_revoked"], ignoreOrder: true);
        ofRefused.Select(row => row.Action).ShouldBe(["mcp.client_registered", "mcp.client_rejected"], ignoreOrder: true);
        ofKept.Concat(ofRefused).ShouldAllBe(row => row.SiteId == null && row.TargetType == "mcp_client");
        ofKept.ShouldAllBe(row => row.TargetLabel == "Kept agent" && row.Details["clientId"] == kept);

        var registered = ofKept.Single(row => row.Action == "mcp.client_registered");
        registered.ActorKind.ShouldBe(PrincipalKind.Anonymous);
        registered.Origin.ShouldBe(Origin.Mcp);
        registered.ClientId.ShouldBe(kept);
        registered.Ip.ShouldBe("10.3.0.3");
        registered.Details["status"].ShouldBe("pending");

        var approval = ofKept.Single(row => row.Action == "mcp.client_approved");
        approval.ActorId.ShouldBe(administrator.Id);
        approval.ActorKind.ShouldBe(PrincipalKind.AdminUser);
        approval.Details["role"].ShouldBe(BuiltInRoles.Author.ToString("D"));
        approval.Details["roleName"].ShouldBe(BuiltInRoles.AuthorName);

        var change = ofKept.Single(row => row.Action == "mcp.client_role_changed");
        change.ActorId.ShouldBe(administrator.Id);
        change.ActorKind.ShouldBe(PrincipalKind.AdminUser);
        change.Details["role"].ShouldBe(BuiltInRoles.Editor.ToString("D"));
        change.Details["roleName"].ShouldBe(BuiltInRoles.EditorName);
        change.Details["previousRole"].ShouldBe(BuiltInRoles.Author.ToString("D"));
        ofKept.Single(row => row.Action == "mcp.client_revoked").ActorId.ShouldBe(administrator.Id);
        ofRefused.Single(row => row.Action == "mcp.client_rejected").ActorId.ShouldBe(administrator.Id);

        // A client the administrator registered is registered by the administrator, through the administrator's own client,
        // and approved at once.
        var byAdministrator = ofDesk.ShouldHaveSingleItem();
        byAdministrator.Action.ShouldBe("mcp.client_registered");
        byAdministrator.SiteId.ShouldBeNull();
        byAdministrator.TargetLabel.ShouldBe("Desk agent");
        byAdministrator.ActorId.ShouldBe(administrator.Id);
        byAdministrator.ActorKind.ShouldBe(PrincipalKind.AdminUser);
        byAdministrator.Origin.ShouldNotBe(Origin.Mcp);
        byAdministrator.ClientId.ShouldNotBe(desk);
        byAdministrator.Ip.ShouldBeNull();
        byAdministrator.Details["clientId"].ShouldBe(desk);
        byAdministrator.Details["status"].ShouldBe("approved");
        byAdministrator.Details["role"].ShouldBe(BuiltInRoles.Editor.ToString("D"));
    }

    [Fact]
    public async Task UnderTheOpenPolicyTheRegistrationRowNamesTheRoleItGave()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.3.0.4");
        var administrator = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var admin = await host.AsUserAsync(administrator);
        var id = await IdOfAsync(admin, await agent.RegisteredAsync());

        var row = (await RowsAsync(host, administrator, id)).ShouldHaveSingleItem();

        row.Action.ShouldBe("mcp.client_registered");
        row.Details["status"].ShouldBe("approved");
        row.Details["role"].ShouldBe(BuiltInRoles.Editor.ToString("D"));
    }

    [Fact]
    public async Task AClientThatIsNotApprovedWithinSevenDaysIsRemovedAndAnApprovedOneStays()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.3.0.5");
        using var admin = await KeyAsync(host);
        var pending = await agent.RegisteredAsync("Forgotten agent");
        var rejected = await agent.RegisteredAsync("Refused agent");
        using var refusal = await RejectAsync(admin, await IdOfAsync(admin, rejected));
        var (approved, _) = await ApprovedAsync(agent, admin, BuiltInRoles.EditorName, "Kept agent");

        host.Time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1));
        var young = await agent.RegisteredAsync("Young agent");
        await RunCleanupAsync();
        var early = await ListAsync(admin);

        host.Time.Advance(TimeSpan.FromMinutes(2));
        await RunCleanupAsync();
        var late = await ListAsync(admin);

        early.Select(item => item!["clientId"]!.GetValue<string>()).ShouldContain(pending);
        late.Select(item => item!["clientId"]!.GetValue<string>()).ShouldNotContain(pending);
        late.Select(item => item!["clientId"]!.GetValue<string>()).ShouldNotContain(rejected);
        late.Select(item => item!["clientId"]!.GetValue<string>()).ShouldContain(approved);
        late.Select(item => item!["clientId"]!.GetValue<string>()).ShouldContain(young);
        (await agent.AuthorizeAsync(pending)).StoppedAt.ShouldBe("/account/error");
    }

    [Fact]
    public async Task TheNumberOfClientsThatWaitForApprovalIsCapped()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.6");
        using var admin = await KeyAsync(host);
        var waiting = (await ListAsync(admin)).Count(item => item!["status"]!.GetValue<string>() == "pending");
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval, maxPending: waiting + 2);

        var first = await agent.RegisteredAsync();
        await agent.RegisteredAsync();
        using var capped = await agent.RegisterAsync(McpAgent.Registration());

        capped.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await JsonAsync(capped))["error"]!.GetValue<string>().ShouldBe("temporarily_unavailable");
        capped.Headers.RetryAfter.ShouldNotBeNull();
        (await ListAsync(admin)).Count(item => item!["status"]!.GetValue<string>() == "pending").ShouldBe(waiting + 2);

        // A client an administrator has answered no longer holds a place.
        using var rejected = await RejectAsync(admin, await IdOfAsync(admin, first));
        using var next = await agent.RegisterAsync(McpAgent.Registration());
        next.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The open policy approves at once, so the cap, which is of clients that wait, does not hold it back.
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxPending: 1);
        using var open = await agent.RegisterAsync(McpAgent.Registration());
        open.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task TheNumberOfAllClientsIsCappedInEveryMode()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.7");
        using var admin = await KeyAsync(host);
        var clients = (await ListAsync(admin)).Count;

        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxClients: clients + 1);
        await agent.RegisteredAsync();
        using var open = await agent.RegisterAsync(McpAgent.Registration());
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval, maxClients: clients + 1);
        using var approval = await agent.RegisterAsync(McpAgent.Registration());

        foreach (var capped in new[] { open, approval })
        {
            capped.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await JsonAsync(capped))["error"]!.GetValue<string>().ShouldBe("temporarily_unavailable");
            capped.Headers.RetryAfter.ShouldNotBeNull();
        }

        (await ListAsync(admin)).Count.ShouldBe(clients + 1);
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval, maxClients: clients + 2);
        using var room = await agent.RegisterAsync(McpAgent.Registration());
        room.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AClientTheOpenPolicyApprovedIsRemovedWhenNobodyConsentedWithinNeverUsedClientHours()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.8");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        var vetted = await agent.RegisteredAsync();
        using var approved = await ApproveAsync(admin, await IdOfAsync(admin, vetted), BuiltInRoles.EditorName);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var filler = await agent.RegisteredAsync();
        var used = await agent.RegisteredAsync();
        await agent.TokensAsync(used, user);

        host.Time.Advance(TimeSpan.FromHours(new McpSettings().NeverUsedClientHours) - TimeSpan.FromMinutes(1));
        await RunCleanupAsync();
        (await FindAsync(admin, filler)).ShouldNotBeNull();

        host.Time.Advance(TimeSpan.FromMinutes(2));
        var young = await agent.RegisteredAsync();
        await RunCleanupAsync();

        var left = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).ToList();
        left.ShouldNotContain(filler);
        left.ShouldContain(used);
        left.ShouldContain(vetted);
        left.ShouldContain(young);
    }

    [Fact]
    public async Task AUsedClientTheOpenPolicyApprovedIsRemovedOnceItsTokensAreGoneForUnusedClientDaysUnlessAnAdministratorChangedIt()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.9");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var left = await agent.RegisteredAsync();
        var changed = await agent.RegisteredAsync();
        await agent.TokensAsync(left, user);
        await agent.TokensAsync(changed, user);
        using var role = await ChangeRoleAsync(admin, await IdOfAsync(admin, changed), BuiltInRoles.AuthorName);
        role.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FindAsync(admin, changed)).ShouldNotBeNull()["approvedBy"].ShouldNotBeNull();

        // As the prune of the authorization server leaves it once the tokens and authorizations have expired.
        await ForgetGrantsAsync(await IdOfAsync(admin, left));
        await ForgetGrantsAsync(await IdOfAsync(admin, changed));
        host.Time.Advance(TimeSpan.FromDays(new McpSettings().UnusedClientDays) - TimeSpan.FromMinutes(1));
        await RunCleanupAsync();
        (await FindAsync(admin, left)).ShouldNotBeNull();

        host.Time.Advance(TimeSpan.FromMinutes(2));
        await RunCleanupAsync();

        (await FindAsync(admin, left)).ShouldBeNull();
        (await FindAsync(admin, changed)).ShouldNotBeNull();
    }

    [Fact]
    public async Task AnAdministratorRegistersAClientAtTheCapWhichCountsIt()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.10");
        using var admin = await KeyAsync(host);
        var clients = (await ListAsync(admin)).Count;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxClients: clients);

        using var registered = await RegisterAsync(admin, Registration("Desk agent", BuiltInRoles.EditorName, McpAgent.RedirectUri));
        using var own = await agent.RegisterAsync(McpAgent.Registration());

        registered.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ListAsync(admin)).Count.ShouldBe(clients + 1);
        own.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ChangingTheRedirectUrisOfAConfiguredClientEndsItsConsents()
    {
        var host = fixture.Host;
        var user = Guid.CreateVersion7();
        await RegisterConfiguredAsync("https://app.example/cb", "Desk");
        await GrantAsync(user);

        await RegisterConfiguredAsync("https://app.example/cb", "Desk renamed");
        (await ConsentAsync(user)).ShouldNotBeNull();

        // A host in another case is the same host, so it is no change of the URIs, whatever else changes with it.
        await RegisterConfiguredAsync("https://APP.example/cb", "Desk renamed again");
        (await ConsentAsync(user)).ShouldNotBeNull();

        await RegisterConfiguredAsync("https://other.example/cb", "Desk renamed again");
        (await ConsentAsync(user)).ShouldBeNull();
    }

    // A redirect URI of a scheme of the application's own has no host, so the page names the scheme.
    [Fact]
    public async Task TheConsentPageNamesARedirectUriWithoutAHostByItsScheme()
    {
        await RegisterConfiguredAsync("com.example.app:/cb", "Native");

        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var request = await scope.ServiceProvider.GetRequiredService<ClientConsents>().DescribeAsync(ConfiguredClient, ["openid"], "com.example.app:/cb", Token);

        request.ShouldNotBeNull().RedirectHosts.ShouldBe(["com.example.app:"]);
    }

    // One call removes the clients the open policy approved that registered in a span of time, used or not, and none
    // that an administrator approved, registered or gave a role, even the role it had.
    [Fact]
    public async Task AnAdministratorRemovesTheClientsTheOpenPolicyApprovedInOneCallAndNoneAnAdministratorActedOn()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.20");
        using var admin = await KeyAsync(host);
        var administrator = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var early = await agent.RegisteredAsync("Early");

        host.Time.Advance(TimeSpan.FromHours(1));
        var from = host.Time.GetUtcNow();
        var filler = await agent.RegisteredAsync("Filler");
        var consented = await agent.RegisteredAsync("Consented");
        var adopted = await agent.RegisteredAsync("Adopted");
        var givenARole = await agent.RegisteredAsync("Given a role");
        var consentedId = await IdOfAsync(admin, consented);
        var tokens = await agent.TokensAsync(consented, user);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        using var sameRole = await ChangeRoleAsync(admin, await IdOfAsync(admin, adopted), BuiltInRoles.EditorName);
        using var otherRole = await ChangeRoleAsync(admin, await IdOfAsync(admin, givenARole), BuiltInRoles.AuthorName);
        var (registered, _) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName);
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        var pending = await agent.RegisteredAsync("Pending");
        var (approved, _) = await ApprovedAsync(agent, admin, BuiltInRoles.EditorName, "Approved");
        var rejected = await agent.RegisteredAsync("Rejected");
        using var refusal = await RejectAsync(admin, await IdOfAsync(admin, rejected));
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);

        host.Time.Advance(TimeSpan.FromHours(1));
        var before = host.Time.GetUtcNow();
        var late = await agent.RegisteredAsync("Late");
        var adoptedBefore = (await FindAsync(admin, adopted)).ShouldNotBeNull();

        using var signedIn = await host.AsUserAsync(administrator);
        using var removed = await RemoveAsync(signedIn, new { registeredFrom = from, registeredBefore = before });

        sameRole.StatusCode.ShouldBe(HttpStatusCode.OK);
        otherRole.StatusCode.ShouldBe(HttpStatusCode.OK);
        adoptedBefore["approvedBy"].ShouldNotBeNull();
        adoptedBefore["approvedAt"]!.GetValue<DateTimeOffset>().ShouldBe(from.AddMinutes(1));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync(Token));
        var answer = await JsonAsync(removed);
        answer["removed"]!.GetValue<int>().ShouldBe(2);
        answer["more"]!.GetValue<bool>().ShouldBeFalse();
        var left = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).ToList();
        left.ShouldNotContain(filler);
        left.ShouldNotContain(consented);
        foreach (var kept in new[] { early, late, adopted, givenARole, registered, pending, approved, rejected })
        {
            left.ShouldContain(kept);
        }

        // Each goes as a single delete removes it: its tokens end at once, and the row names the administrator.
        using var http = agent.Bearer(tokens["access_token"]!.GetValue<string>());
        using var cut = await PostAsync(http);
        cut.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var row = (await RowsAsync(host, administrator, consentedId)).Single(row => row.Action == "mcp.client_revoked");
        row.ActorId.ShouldBe(administrator.Id);
        row.TargetLabel.ShouldBe("Consented");
    }

    // The bulk removal is held to the permission of the single delete, and to a span of time: a call that names none
    // would remove every such client.
    [Fact]
    public async Task TheBulkRemovalNeedsMcpManageForTheInstanceAndATime()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.21");
        using var admin = await KeyAsync(host);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var filler = await agent.RegisteredAsync();
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var now = host.Time.GetUtcNow();
        using var editor = await host.AsUserAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));
        var siteManager = await McpSiteTesting.CreateRoleAsync(host, (McpPermissions.Manage, new PermissionConstraints { Sites = [new SiteId(DefaultSite.Id)] }));
        using var siteGrant = await McpSiteTesting.KeyInAsync(host, DefaultSite.Id, siteManager);
        using var boundKey = await BoundKeyAsync(admin, DefaultSite.Id);
        using var anonymous = host.CreateClient();

        foreach (var caller in new[] { editor, siteGrant, boundKey })
        {
            using var refused = await RemoveAsync(caller, new { registeredBefore = now });
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await CodeAsync(refused)).ShouldBe("FCMS3103");
        }

        using var unsigned = await RemoveAsync(anonymous, new { registeredBefore = now });
        using var noTime = await RemoveAsync(admin, new { });
        using var noBody = await admin.PostAsync(new Uri($"{ClientsPath}/remove", UriKind.Relative), null, Token);
        using var empty = await RemoveAsync(admin, new { registeredFrom = now, registeredBefore = now });
        using var backwards = await RemoveAsync(admin, new { registeredFrom = now, registeredBefore = now.AddHours(-1) });

        // A time without an offset would be read in the zone of the server, and name another span.
        using var noOffset = await RemoveAsync(admin, new { registeredBefore = "2999-03-01T09:00:00" });
        using var noOffsetFrom = await RemoveAsync(admin, new { registeredFrom = "2000-03-01T09:00:00", registeredBefore = now });
        using var notATime = await RemoveAsync(admin, new { registeredBefore = "tomorrow" });

        unsigned.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (string Name, HttpResponseMessage Response, string Says)[] refusals =
        [
            (nameof(noTime), noTime, "'registeredFrom', 'registeredBefore' or both"),
            (nameof(noBody), noBody, "'registeredFrom', 'registeredBefore' or both"),
            (nameof(empty), empty, "must be earlier than 'registeredBefore'"),
            (nameof(backwards), backwards, "must be earlier than 'registeredBefore'"),
            (nameof(noOffset), noOffset, "'registeredBefore' is an ISO 8601 date and time with an offset"),
            (nameof(noOffsetFrom), noOffsetFrom, "'registeredFrom' is an ISO 8601 date and time with an offset"),
            (nameof(notATime), notATime, "'registeredBefore' is an ISO 8601 date and time with an offset"),
        ];
        foreach (var (name, response, says) in refusals)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, name);
            var problem = await JsonAsync(response);
            problem["code"]!.GetValue<string>().ShouldBe("FCMS1610", name);
            problem["title"]!.GetValue<string>().ShouldBe("MCP client removal span invalid", name);
            problem["detail"]!.GetValue<string>().ShouldContain(says, customMessage: name);
        }

        (await FindAsync(admin, filler)).ShouldNotBeNull();

        using var removed = await RemoveAsync(admin, new { registeredBefore = now });
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FindAsync(admin, filler)).ShouldBeNull();
    }

    // A consent keeps a client the open policy approved from the cleanup, so a user holds only so many of them: one
    // more is refused and stays a client never used, which the cleanup removes.
    [Fact]
    public async Task AUserKeepsOnlyMaxConsentsPerUserClientsOfTheOpenPolicyAliveByConsenting()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.22");
        using var neighbourAgent = new McpAgent(host, "10.3.0.23");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var neighbour = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxConsents: 2);
        var first = await agent.RegisteredAsync("First");
        var second = await agent.RegisteredAsync("Second");
        var third = await agent.RegisteredAsync("Third");
        var fourth = await agent.RegisteredAsync("Fourth");
        var (vetted, _) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName);
        var thirdId = await IdOfAsync(admin, third);

        await agent.TokensAsync(first, user);
        await agent.TokensAsync(second, user);
        var overTheCap = await agent.AuthorizeAsync(third);
        var neverUsed = (await StateAsync(host, thirdId)).UsedAt;

        // What does not add a client to those the user keeps is not held back: another consent to one of them, a consent
        // to a client an administrator registered, and the consent of another user.
        var moreScopes = await agent.AuthorizeAsync(first, scope: "openid mcp profile");
        await agent.TokensAsync(vetted, user);
        await neighbourAgent.TokensAsync(fourth, neighbour);

        overTheCap.Code.ShouldBeNull();
        overTheCap.StoppedAt.ShouldBe("/account/error");
        neverUsed.ShouldBeNull();
        moreScopes.Code.ShouldNotBeNull();

        host.Time.Advance(TimeSpan.FromHours(new McpSettings().NeverUsedClientHours) + TimeSpan.FromMinutes(1));
        await RunCleanupAsync();
        var left = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).ToList();
        left.ShouldNotContain(third);
        left.ShouldContain(first);
        left.ShouldContain(second);
        left.ShouldContain(fourth);

        // A place is free again once one of the clients is gone.
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxConsents: 2);
        var fifth = await agent.RegisteredAsync("Fifth");
        await agent.SignInAsync(user);
        (await agent.AuthorizeAsync(fifth)).StoppedAt.ShouldBe("/account/error");
        using var revoked = await RevokeAsync(admin, await IdOfAsync(admin, first));
        (await agent.AuthorizeAsync(fifth)).Code.ShouldNotBeNull();
    }

    // Ending a consent frees no place while the client it kept is there: not while the revoked authorization is, and not
    // once the prune has taken it and only the client's state says whose it was. Otherwise a user would consent, end the
    // consent and consent to the next clients, each of which stays for the cleanup's long period.
    [Fact]
    public async Task EndingAConsentFreesNoPlaceWhileTheClientItKeptIsThere()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.26");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxConsents: 2);
        var first = await agent.RegisteredAsync("First");
        var second = await agent.RegisteredAsync("Second");
        var third = await agent.RegisteredAsync("Third");
        await agent.TokensAsync(first, user);
        await agent.TokensAsync(second, user);

        // As a change of the password, or the end of each session, ends them.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<UserSessions>().RevokeAllAsync(user.Id, Token);
        }

        var afterTheRevoke = await agent.AuthorizeAsync(third);
        var sameClient = await agent.AuthorizeAsync(first);

        await ForgetGrantsAsync(await IdOfAsync(admin, first));
        await ForgetGrantsAsync(await IdOfAsync(admin, second));
        var afterThePrune = await agent.AuthorizeAsync(third);
        var sameClientAgain = await agent.AuthorizeAsync(first);

        afterTheRevoke.Code.ShouldBeNull();
        afterTheRevoke.StoppedAt.ShouldBe("/account/error");
        sameClient.Code.ShouldNotBeNull();
        afterThePrune.Code.ShouldBeNull();
        afterThePrune.StoppedAt.ShouldBe("/account/error");
        sameClientAgain.Code.ShouldNotBeNull();

        // The place is free once the client is gone.
        using var revoked = await RevokeAsync(admin, await IdOfAsync(admin, second));
        (await agent.AuthorizeAsync(third)).Code.ShouldNotBeNull();
    }

    // One call removes so many clients, each in a transaction of its own, and says that it stopped there.
    [Fact]
    public async Task ABulkRemovalStopsAtItsCeilingAndSaysThatMoreRemain()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.27");
        using var admin = await KeyAsync(host);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var from = host.Time.GetUtcNow();
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, perHour: 1000);
        for (var index = 0; index <= McpClientService.MaxRemovedPerCall; index++)
        {
            await agent.RegisteredAsync();
        }

        using var first = await RemoveAsync(admin, new { registeredFrom = from });
        using var second = await RemoveAsync(admin, new { registeredFrom = from });
        using var third = await RemoveAsync(admin, new { registeredFrom = from });

        var answers = new[] { await JsonAsync(first), await JsonAsync(second), await JsonAsync(third) };
        answers.Select(answer => answer["removed"]!.GetValue<int>()).ShouldBe([McpClientService.MaxRemovedPerCall, 1, 0]);
        answers.Select(answer => answer["more"]!.GetValue<bool>()).ShouldBe([true, false, false]);
    }

    // A client that registered before its first use was recorded has no usedAt. While it holds a consent the cleanup
    // writes one, so that it is kept for UnusedClientDays once its grants are gone; one whose grants were gone already
    // cannot be told from a client never used, and goes after NeverUsedClientHours.
    [Fact]
    public async Task AClientUsedBeforeItsFirstUseWasRecordedIsMarkedByTheCleanupWhileItHoldsAGrant()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.24");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var held = await agent.RegisteredAsync("Holds a consent");
        var bare = await agent.RegisteredAsync("Holds nothing");
        var heldId = await IdOfAsync(admin, held);
        var bareId = await IdOfAsync(admin, bare);
        var consentedAt = host.Time.GetUtcNow();
        await agent.TokensAsync(held, user);
        await agent.TokensAsync(bare, user);
        await ForgetFirstUseAsync(host, heldId);
        await ForgetFirstUseAsync(host, bareId);
        await ForgetGrantsAsync(bareId);

        host.Time.Advance(TimeSpan.FromMinutes(5));
        await RunCleanupAsync();
        var marked = (await StateAsync(host, heldId)).UsedAt;
        var markedBy = (await StateAsync(host, heldId)).UsedBy;
        var unmarked = (await StateAsync(host, bareId)).UsedAt;

        await ForgetGrantsAsync(heldId);
        host.Time.Advance(TimeSpan.FromHours(new McpSettings().NeverUsedClientHours));
        await RunCleanupAsync();
        var afterTheShortPeriod = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).ToList();

        host.Time.Advance(TimeSpan.FromDays(new McpSettings().UnusedClientDays));
        await RunCleanupAsync();

        // The time of the first consent, not of the run that wrote it, and the user who gave it.
        (marked.ShouldNotBeNull() - consentedAt).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(1));
        markedBy.ShouldBe(user.Id);
        unmarked.ShouldBeNull();
        afterTheShortPeriod.ShouldContain(held);
        afterTheShortPeriod.ShouldNotContain(bare);
        (await FindAsync(admin, held)).ShouldBeNull();
    }

    // A role given with no operation, as by code of the host, is the system's act: the client is not taken for one the
    // open policy approved, so the bulk removal leaves it.
    [Fact]
    public async Task ARoleGivenWithoutAnOperationCountsAsAnAdministratorsAct()
    {
        var host = fixture.Host;
        using var agent = new McpAgent(host, "10.3.0.25");
        using var admin = await KeyAsync(host);
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        var clientId = await agent.RegisteredAsync();
        var id = await IdOfAsync(admin, clientId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IMcpClients>().ChangeRoleAsync(id, BuiltInRoles.AuthorName, Token)).Outcome.ShouldBe(McpClientChangeOutcome.Done);
        }

        host.Time.Advance(TimeSpan.FromMinutes(1));
        using var removed = await RemoveAsync(admin, new { registeredBefore = host.Time.GetUtcNow() });

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var client = (await FindAsync(admin, clientId)).ShouldNotBeNull();
        client["approvedBy"]!.GetValue<Guid>().ShouldBe(Principal.System.Id);
        client["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.AuthorName);
    }

    private Task RegisterConfiguredAsync(string redirectUri, string name) => McpClientTesting.RegisterConfiguredAsync(fixture.Host, redirectUri, name);

    private Task GrantAsync(Guid user) => McpClientTesting.GrantAsync(fixture.Host, user);

    private Task<object?> ConsentAsync(Guid user) => McpClientTesting.ConsentAsync(fixture.Host, user);

    private Task ForgetGrantsAsync(Guid application) => McpClientTesting.ForgetGrantsAsync(fixture.Host, application);

    private async Task<IReadOnlyList<UserSession>> SessionsAsync(Guid user)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserSessions>().ListAsync(user, Token);
    }

    // A Management key of Super Admin bound to one site, made through the Management API as an administrator would.
    private async Task<HttpClient> BoundKeyAsync(HttpClient admin, Guid site)
    {
        var key = await McpSiteTesting.SendAsync(
            admin,
            HttpMethod.Post,
            "/api/management/v1/api-keys",
            new { name = "bound-" + Guid.NewGuid().ToString("N"), @class = "management", roleId = BuiltInRoles.SuperAdmin, siteIds = new[] { site } },
            HttpStatusCode.Created);
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", key!["secret"]!.GetValue<string>());
        return client;
    }

    private Task RunCleanupAsync() => McpClientTesting.RunCleanupAsync(fixture.Host);
}
