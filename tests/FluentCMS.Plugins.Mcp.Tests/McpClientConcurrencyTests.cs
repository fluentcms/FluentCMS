using System.Net;
using FluentCMS.Agents;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Plugins.Sample;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    // to the other clients, and the one it could not delete is left for the next run and named in the log.
    [Fact]
    public async Task ADeleteTheDatabaseRefusesDoesNotEndTheCleanup()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.5.0.4");
        using var admin = await KeyAsync(host);
        string[] fillers = [await agent.RegisteredAsync(), await agent.RegisteredAsync()];
        host.Time.Advance(TimeSpan.FromHours(new McpSettings().NeverUsedClientHours) + TimeSpan.FromMinutes(1));

        fixture.Hooks.FailNextDelete(of: await IdOfAsync(admin, fillers[0]));
        await RunCleanupAsync(host);
        var afterTheRefusal = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).Where(fillers.Contains).ToList();
        var skipped = await IdOfAsync(admin, afterTheRefusal[0]);
        await RunCleanupAsync(host);
        var afterTheNextRun = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).Where(fillers.Contains).ToList();

        afterTheRefusal.Count.ShouldBe(1);
        afterTheNextRun.ShouldBeEmpty();
        fixture.Logs.Lines.Count(line => line.Id == 9009 && line.Level == LogLevel.Warning && line.Message.Contains(skipped.ToString("D"), StringComparison.Ordinal)).ShouldBe(1);
    }

    // The bulk removal goes on past a client it cannot delete, whether the database refused the delete, which is logged
    // with the client's id, or the client changed since it was read; either is left for the next call.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADeleteThatFailsDoesNotEndTheBulkRemoval(bool changedMeanwhile)
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, changedMeanwhile ? "10.5.0.6" : "10.5.0.5");
        using var admin = await KeyAsync(host);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var from = host.Time.GetUtcNow();
        string[] fillers = [await agent.RegisteredAsync(), await agent.RegisteredAsync()];
        var ids = new[] { await IdOfAsync(admin, fillers[0]), await IdOfAsync(admin, fillers[1]) };
        var logged = fixture.Logs.Lines.Count(line => line.Id == 9009);

        fixture.Hooks.FailNextDelete(changedMeanwhile ? DeleteFailure.ChangedMeanwhile : DeleteFailure.ForeignKey);
        using var crossed = await RemoveAsync(admin, new { registeredFrom = from });
        var left = (await ListAsync(admin)).Where(item => fillers.Contains(item!["clientId"]!.GetValue<string>())).Select(item => item!["id"]!.GetValue<Guid>()).ToList();
        using var again = await RemoveAsync(admin, new { registeredFrom = from });

        crossed.StatusCode.ShouldBe(HttpStatusCode.OK, await crossed.Content.ReadAsStringAsync(Token));
        (await JsonAsync(crossed))["removed"]!.GetValue<int>().ShouldBe(1);
        var skipped = left.ShouldHaveSingleItem();
        ids.ShouldContain(skipped);
        (await JsonAsync(again))["removed"]!.GetValue<int>().ShouldBe(1);
        fixture.Logs.Lines.Count(line => line.Id == 9009).ShouldBe(changedMeanwhile ? logged : logged + 1);
        fixture.Logs.Lines.Any(line => line.Id == 9009 && line.Message.Contains(skipped.ToString("D"), StringComparison.Ordinal)).ShouldBe(!changedMeanwhile);
    }

    // Two consents of one user at once, each of which would take the last place. They are let go only once the first holds
    // its insert and the second has either read the cap too, which lets both through, or waits for the lock of the first,
    // so the one moment the cap could be passed is the one the test makes. One of them takes the place, and the other is
    // refused.
    [Fact]
    public async Task TwoConsentsAtOnceTakeNoMoreThanTheLastPlace()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName, maxConsents: 1);
        using var agent = new McpAgent(host, "10.5.0.7");
        using var admin = await KeyAsync(host);
        var user = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        string[] clients = [await agent.RegisteredAsync("First"), await agent.RegisteredAsync("Second")];

        fixture.Consents.Arm();
        Task<ConsentGrant> first;
        Task<ConsentGrant> second;
        try
        {
            first = Task.Run(() => GiveConsentAsync(host, user.Id, clients[0]), Token);
            await fixture.Consents.FirstInsert.WaitAsync(Patience, Token);
            second = Task.Run(() => GiveConsentAsync(host, user.Id, clients[1]), Token);
            await Task.WhenAny(fixture.Consents.SecondInsert, fixture.Consents.SecondLock).WaitAsync(Patience, Token);
        }
        finally
        {
            fixture.Consents.Release();
        }

        var granted = await Task.WhenAll(first, second).WaitAsync(Patience, Token);

        granted.Order().ShouldBe([ConsentGrant.Granted, ConsentGrant.LimitReached]);
        var places = 0;
        foreach (var clientId in clients)
        {
            places += (await StateAsync(host, await IdOfAsync(admin, clientId))).UsedBy == user.Id ? 1 : 0;
        }

        places.ShouldBe(1);
        fixture.Logs.Lines.ShouldContain(line => line.Id == 9008 && line.Message.Contains(user.Id.ToString("D"), StringComparison.Ordinal));
    }

    // A call reads again, each in a transaction of its own, only the clients it would remove: those the open policy
    // approved in its span, and no client outside it, no pending one, and none an administrator acted on.
    [Fact]
    public async Task TheBulkRemovalReadsAgainOnlyTheClientsOfItsSpan()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.5.0.8");
        using var admin = await KeyAsync(host);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        await agent.RegisteredAsync("Before the span");
        await agent.RegisteredAsync("Before the span too");
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var from = host.Time.GetUtcNow();
        string[] inTheSpan = [await agent.RegisteredAsync("In the span"), await agent.RegisteredAsync("In the span too")];
        var (adopted, _) = await RegisteredByAdminAsync(admin, BuiltInRoles.EditorName);
        await SetPolicyAsync(host, McpClientRegistrationMode.Approval);
        var pending = await agent.RegisteredAsync("Pending");

        fixture.Hooks.CountFinds();
        using var removed = await RemoveAsync(admin, new { registeredFrom = from });
        var reads = fixture.Hooks.StopCounting();

        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync(Token));
        (await JsonAsync(removed))["removed"]!.GetValue<int>().ShouldBe(2);
        reads.ShouldBe(inTheSpan.Length);
        var left = (await ListAsync(admin)).Select(item => item!["clientId"]!.GetValue<string>()).ToList();
        left.ShouldContain(adopted);
        left.ShouldContain(pending);
    }

    // Only the refusal of a foreign key is taken for a consent that arrived during the delete. Any other failure of the
    // save ends the bulk removal and the cleanup, rather than answering as if the client were left for the next call.
    [Fact]
    public async Task AFailureOfADeleteOtherThanAForeignKeyEndsTheBulkRemovalAndTheCleanup()
    {
        var host = fixture.Host;
        await SetPolicyAsync(host, McpClientRegistrationMode.Open, BuiltInRoles.EditorName);
        using var agent = new McpAgent(host, "10.5.0.9");
        using var admin = await KeyAsync(host);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var from = host.Time.GetUtcNow();
        var filler = await agent.RegisteredAsync();
        var id = await IdOfAsync(admin, filler);
        var logged = fixture.Logs.Lines.Count(line => line.Id == 9009);

        fixture.Hooks.FailNextDelete(DeleteFailure.OtherConstraint, id);
        using var failed = await RemoveAsync(admin, new { registeredFrom = from });
        var afterTheCall = await FindAsync(admin, filler);

        host.Time.Advance(TimeSpan.FromHours(new McpSettings().NeverUsedClientHours) + TimeSpan.FromMinutes(1));
        fixture.Hooks.FailNextDelete(DeleteFailure.OtherConstraint, id);
        var cleanup = await Should.ThrowAsync<DbUpdateException>(() => RunCleanupAsync(host));
        var afterTheRun = await FindAsync(admin, filler);

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError, await failed.Content.ReadAsStringAsync(Token));
        afterTheCall.ShouldNotBeNull();
        cleanup.InnerException.ShouldBeOfType<SqliteException>().SqliteExtendedErrorCode.ShouldBe(275);
        afterTheRun.ShouldNotBeNull();
        fixture.Logs.Lines.Count(line => line.Id == 9009).ShouldBe(logged);
        (await StateAsync(host, id)).Status.ShouldBe(McpClientStatus.Approved);
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

    // Bounds every wait of the races, so that one that hangs fails its test.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static async Task<ConsentGrant> GiveConsentAsync(FluentCmsTestHost host, Guid user, string clientId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ClientConsents>().GrantAsync(user, clientId, ["openid", "mcp"], Token);
    }
}
