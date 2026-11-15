using System.Net;
using System.Net.Http.Headers;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Sites;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
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

    // The job as the runner runs it: as the system, in a scope of its own.
    private async Task RunCleanupAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        using var system = SystemOperation.Begin(accessor, Origin.Job, new SiteId(DefaultSite.Id));
        var job = ActivatorUtilities.CreateInstance<McpClientCleanupJob>(scope.ServiceProvider);
        await job.ExecuteAsync(new McpClientCleanupPayload(), new JobContext(Guid.CreateVersion7(), McpClientCleanupJob.Type, "default", 1, 1, accessor.Current!, Token));
    }
}
