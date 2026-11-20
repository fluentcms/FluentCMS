using System.Net;
using FluentCMS.Agents;
using FluentCMS.Core.Authorization;
using FluentCMS.Plugins.Sample;
using OpenIddict.Abstractions;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpClientTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// A role change that crosses a refresh of the client, or another change of the client.
public sealed class McpClientConcurrencyTests(McpClientHookFixture fixture) : IClassFixture<McpClientHookFixture>
{
    // The token endpoint reads the client's role before it issues the tokens, so a refresh can read the old role and issue
    // its tokens after the change has committed. They are issued under the authorization the change revoked, so they are
    // refused at their first use, and the user consents again.
    [Fact]
    public async Task TokensIssuedWithTheOldRoleAfterARoleChangeAreRefusedAtTheirFirstUse()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.5.0.1");
        using var admin = await KeyAsync(host);
        var readerName = "Reader " + Guid.NewGuid().ToString("N")[..6];
        await host.CreateRoleAsync(readerName, ["content.article.read"], Token);
        var superAdmin = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var (clientId, id) = await RegisteredByAdminAsync(admin, readerName);
        var tokens = await agent.TokensAsync(clientId, superAdmin);

        fixture.Hooks.BeforeNextPropertiesRead(async () =>
        {
            using var changed = await ChangeRoleAsync(admin, id, BuiltInRoles.SuperAdminName);
            changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync(Token));
        });
        using var raced = await agent.RefreshAsync(clientId, tokens["refresh_token"]!.GetValue<string>());

        raced.StatusCode.ShouldBe(HttpStatusCode.OK, await raced.Content.ReadAsStringAsync(Token));
        var issued = await JsonAsync(raced);
        using var http = agent.Bearer(issued["access_token"]!.GetValue<string>());
        using var used = await PostAsync(http);
        using var refreshed = await agent.RefreshAsync(clientId, issued["refresh_token"]!.GetValue<string>());
        used.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(refreshed))["error"]!.GetValue<string>().ShouldBe("invalid_grant");

        // The consent went with the old role: the user is asked again, and the token after it carries the new role.
        (await agent.AuthorizeAsync(clientId, allow: false)).Error.ShouldBe("access_denied");
        var after = await agent.TokensAsync(clientId, superAdmin);
        using var allowed = agent.Bearer(after["access_token"]!.GetValue<string>());
        await using var mcp = await ConnectAsync(allowed, protocolVersion: InitializeProtocol);
        var created = await mcp.CallToolAsync(SampleTools.EntryCreate, new Dictionary<string, object?> { ["type"] = "article", ["title"] = "After the race" }, cancellationToken: Token);
        created.IsError.ShouldBe(false, Text(created));
    }

    // A change that crosses another change of the client is refused and rolls back whole: the role, the tokens and the
    // consent stay, and no row is written.
    [Fact]
    public async Task ARoleChangeThatCrossesAnotherChangeIsRefusedAndChangesNothing()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        using var agent = new McpAgent(host, "10.5.0.2");
        var administrator = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var admin = await host.AsUserAsync(administrator);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var (clientId, id) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName);
        var tokens = await agent.TokensAsync(clientId, user);

        fixture.Hooks.FailNextUpdate();
        using var crossed = await ChangeRoleAsync(admin, id, BuiltInRoles.AuthorName);

        crossed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(crossed)).ShouldBe("FCMS1609");
        (await FindAsync(admin, clientId)).ShouldNotBeNull()["roleName"]!.GetValue<string>().ShouldBe(BuiltInRoles.EditorName);
        using (var http = agent.Bearer(tokens["access_token"]!.GetValue<string>()))
        {
            using var working = await PostAsync(http);
            working.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var refreshed = await agent.RefreshAsync(clientId, tokens["refresh_token"]!.GetValue<string>());
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Without a consent page: refusing on it would end with access_denied instead of a code.
        (await agent.AuthorizeAsync(clientId, allow: false)).Code.ShouldNotBeNull();
        (await RowsAsync(host, administrator, id)).Select(row => row.Action).ShouldNotContain("mcp.client_role_changed");
    }

    // The write of a client's first use is not tried again when it crosses another write of the client. The consent
    // stands, so the next cleanup writes it, and the client is not taken for one never used.
    [Fact]
    public async Task AFirstUseWhoseWriteCrossedAnotherIsRecordedByTheCleanup()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.5.0.3");
        using var admin = await KeyAsync(host);
        var clientId = await agent.RegisteredAsync();
        var id = await IdOfAsync(admin, clientId);
        await agent.SignInAsync(await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token));

        fixture.Hooks.FailNextUpdate();
        var authorized = await agent.AuthorizeAsync(clientId);
        var crossed = (await StateAsync(host, id)).UsedAt;
        await RunCleanupAsync(host);

        authorized.Code.ShouldNotBeNull();
        crossed.ShouldBeNull();
        (await StateAsync(host, id)).UsedAt.ShouldNotBeNull();
    }

    // A consent that arrives while the cleanup deletes its client makes the database refuse the delete. The run goes on
    // to the other clients, and the one it could not delete is left for the next run.
    [Fact]
    public async Task ADeleteTheDatabaseRefusesDoesNotEndTheCleanup()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.5.0.4");
        using var admin = await KeyAsync(host);
        string[] fillers = [await agent.RegisteredAsync(), await agent.RegisteredAsync()];
        host.Time.Advance(TimeSpan.FromHours(new McpSettings().NeverUsedClientHours) + TimeSpan.FromMinutes(1));

        fixture.Hooks.FailNextDelete();
        await RunCleanupAsync(host);
        var afterTheRefusal = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).Where(fillers.Contains).ToList();
        await RunCleanupAsync(host);
        var afterTheNextRun = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).Where(fillers.Contains).ToList();

        afterTheRefusal.Count.ShouldBe(1);
        afterTheNextRun.ShouldBeEmpty();
    }

    // The consents go before the client is written, so a start that fails on the write leaves the old redirect URIs with
    // no consent, never the new ones with the old consents, and the next start writes the client.
    [Fact]
    public async Task AStartThatFailsWhileTheRedirectUrisChangeLeavesNoConsentBehind()
    {
        var host = fixture.Host;
        var user = Guid.CreateVersion7();
        await RegisterConfiguredAsync(host, "https://app.example/cb", "Desk");
        await GrantAsync(host, user);

        fixture.Hooks.FailNextUpdate();
        await Should.ThrowAsync<OpenIddictExceptions.ConcurrencyException>(() => RegisterConfiguredAsync(host, "https://other.example/cb", "Desk"));
        var consent = await ConsentAsync(host, user);
        var failed = await ConfiguredRedirectUrisAsync(host);
        await RegisterConfiguredAsync(host, "https://other.example/cb", "Desk");

        consent.ShouldBeNull();
        failed.ShouldBe(["https://app.example/cb"]);
        (await ConfiguredRedirectUrisAsync(host)).ShouldBe(["https://other.example/cb"]);
    }
}
