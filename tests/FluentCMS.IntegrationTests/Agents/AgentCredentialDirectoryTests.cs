using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Agents.AgentCredentialScenario;

namespace FluentCMS.IntegrationTests.Agents;

public sealed class AgentCredentialDirectoryTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_issued_credential_reads_back_and_its_key_is_shown_once_and_stored_only_as_a_hash(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);

        var issued = await scenario.IssueAsync();
        var read = (await scenario.RunAsync(directory => directory.GetAsync(issued.Credential.Id))).Value;

        Assert.True(AgentKeys.IsWellFormed(issued.Key));
        Assert.Equal(issued.Credential.Id, read.Id);
        Assert.Equal("catalog-sync", read.DisplayName);
        Assert.Equal([Site.Default.Id], read.Scope.Sites);
        Assert.Equal(["product", "category"], read.Scope.Types);
        Assert.Equal(["product.price"], read.Scope.Fields);
        Assert.Equal([AgentOperation.Read, AgentOperation.Update], read.Scope.Operations);
        Assert.Equal(CatalogSync().Caps, read.Caps);
        Assert.Equal(Start, read.IssuedAt);
        Assert.Equal(Start.AddDays(90), read.ExpiresAt);
        Assert.Null(read.RevokedAt);
        Assert.Equal([issued.Credential.Id], (await scenario.RunAsync(directory => directory.ListAsync())).Select(credential => credential.Id));

        var row = Assert.Single(await scenario.RowsAsync());
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(issued.Key)), row.KeyHash);
        Assert.True(await scenario.StoresTextAsync("catalog-sync"));
        Assert.False(await scenario.StoresTextAsync(issued.Key));
        Assert.False(await scenario.StoresTextAsync(issued.Key[AgentKeys.Prefix.Length..]));

        var issuing = Assert.Single(await scenario.CredentialEventsAsync());
        Assert.Equal("agent_credential.issued", issuing.Operation);
        Assert.Equal(issued.Credential.Id, issuing.SubjectId);
        Assert.Equal(InitiatorKinds.User, issuing.InitiatorKind);
        Assert.Equal(Amir.UserId, issuing.InitiatorId);
        Assert.Null(issuing.AgentCredentialId);

        var missing = await scenario.RunAsync(directory => directory.GetAsync(Guid.NewGuid()));
        Assert.Equal(AgentCredentialErrorCodes.CredentialNotFound, Assert.Single(missing.Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_key_finds_its_own_credential_and_every_other_key_is_refused(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var first = await scenario.IssueAsync();
        var second = await scenario.IssueAsync(CatalogSync() with { DisplayName = "price-check" });
        var events = (await scenario.CredentialEventsAsync()).Count;

        var found = (await scenario.RunAsync(directory => directory.AuthenticateAsync(first.Key))).Value;
        Assert.Equal(first.Credential.Id, found.Id);
        Assert.Equal(first.Credential.Initiator, found.Initiator);
        Assert.Equal(second.Credential.Id, (await scenario.RunAsync(directory => directory.AuthenticateAsync(second.Key))).Value.Id);

        string[] refused = [AgentKeys.Generate(), "", "   ", "not a key", first.Key + "A", first.Key[..^1], first.Key.ToUpperInvariant()];
        foreach (var key in refused)
        {
            var result = await scenario.RunAsync(directory => directory.AuthenticateAsync(key));
            Assert.Equal(AgentCredentialErrorCodes.KeyInvalid, Assert.Single(result.Errors).Code);
            Assert.DoesNotContain(first.Key[AgentKeys.Prefix.Length..^1], result.Errors[0].Message, StringComparison.Ordinal);
        }

        Assert.Equal(events, (await scenario.CredentialEventsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_credential_is_refused_once_its_expiry_is_reached(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var issued = await scenario.IssueAsync(CatalogSync(Start.AddDays(1)));

        scenario.Clock.Now = Start.AddDays(1).AddSeconds(-1);
        Assert.True((await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key))).IsSuccess);

        foreach (var now in new[] { Start.AddDays(1), Start.AddDays(30) })
        {
            scenario.Clock.Now = now;
            var result = await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key));

            Assert.Equal(AgentCredentialErrorCodes.Expired, Assert.Single(result.Errors).Code);
            Assert.DoesNotContain(issued.Key, result.Errors[0].Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revoked_credential_is_refused_at_once_and_its_revocation_is_audited(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var issued = await scenario.IssueAsync();
        var revokedAt = Start.AddHours(1);
        scenario.Clock.Now = revokedAt;

        var revoked = (await scenario.RunAsync(directory => directory.RevokeAsync(issued.Credential.Id))).Value;

        Assert.Equal(revokedAt, revoked.RevokedAt);
        Assert.Equal(revokedAt, (await scenario.RunAsync(directory => directory.GetAsync(issued.Credential.Id))).Value.RevokedAt);
        var refused = await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key));
        Assert.Equal(AgentCredentialErrorCodes.Revoked, Assert.Single(refused.Errors).Code);

        var revocation = Assert.Single(await scenario.CredentialEventsAsync(), row => row.Operation == "agent_credential.revoked");
        Assert.Equal(issued.Credential.Id, revocation.SubjectId);
        Assert.Equal(Amir.UserId, revocation.InitiatorId);
        Assert.Equal(revokedAt, revocation.Timestamp);

        scenario.Clock.Now = revokedAt.AddHours(1);
        var again = await scenario.RunAsync(directory => directory.RevokeAsync(issued.Credential.Id));
        var unknown = await scenario.RunAsync(directory => directory.RevokeAsync(Guid.NewGuid()));

        Assert.Equal(AgentCredentialErrorCodes.AlreadyRevoked, Assert.Single(again.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.CredentialNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(revokedAt, Assert.Single(await scenario.RowsAsync()).RevokedAt);
        Assert.Equal(2, (await scenario.CredentialEventsAsync()).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Invalid_input_writes_nothing_and_appends_no_event(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var unknownSite = Guid.NewGuid();
        var otherSites = AgentScope.Create([Site.Default.Id, unknownSite], null, null, [AgentOperation.Read]).Value;

        var past = await scenario.RunAsync(directory => directory.IssueAsync(CatalogSync(Start) with { DisplayName = " " }));
        var sites = await scenario.RunAsync(directory => directory.IssueAsync(CatalogSync() with { Scope = otherSites }));

        Assert.Equal([AgentCredentialErrorCodes.DisplayNameMissing, AgentCredentialErrorCodes.ExpiryNotInFuture], past.Errors.Select(error => error.Code));
        Assert.Equal(AgentCredentialErrorCodes.ScopeSiteUnknown, Assert.Single(sites.Errors).Code);
        Assert.Contains(unknownSite.ToString(), sites.Errors[0].Message);
        Assert.Empty(await scenario.RowsAsync());
        Assert.Empty(await scenario.CredentialEventsAsync());
    }

    // A credential grants permissions, so an agent is refused whatever its own scope says (SEC-02).
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_can_neither_issue_nor_revoke_a_credential(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var issued = await scenario.IssueAsync();
        scenario.Initiator = Agent;

        var issue = await scenario.RunAsync(directory => directory.IssueAsync(CatalogSync()));
        var revoke = await scenario.RunAsync(directory => directory.RevokeAsync(issued.Credential.Id));

        Assert.Equal(AgentCredentialErrorCodes.PeopleOnly, Assert.Single(issue.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.PeopleOnly, Assert.Single(revoke.Errors).Code);
        Assert.Null(Assert.Single(await scenario.RowsAsync()).RevokedAt);
        Assert.Single(await scenario.CredentialEventsAsync());
        Assert.True((await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key))).IsSuccess);
    }

    // The audit log refuses to append without an initiator, after the row was added to the unit of work.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_while_issuing_leaves_no_credential_behind(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        scenario.Initiator = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(directory => directory.IssueAsync(CatalogSync())));

        Assert.Empty(await scenario.RowsAsync());
        Assert.Empty(await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking().ToListAsync()));
    }

    // The revocation reaches the database before the audit log refuses to append without an initiator.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_mid_revoke_leaves_the_credential_active_and_appends_no_event(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var issued = await scenario.IssueAsync();
        var id = issued.Credential.Id;

        scenario.Initiator = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync(directory => directory.RevokeAsync(id)));

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().RevokeAsync(id));

            // Inside the still open transaction the update is visible, so the failure came after it reached the database.
            Assert.NotNull(await context.AgentCredentials.AsNoTracking().Where(row => row.Id == id).Select(row => row.RevokedAt).SingleAsync());
        }

        scenario.Initiator = Amir;
        Assert.Null(Assert.Single(await scenario.RowsAsync()).RevokedAt);
        Assert.Single(await scenario.CredentialEventsAsync());
        Assert.True((await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key))).IsSuccess);
        Assert.True((await scenario.RunAsync(directory => directory.RevokeAsync(id))).IsSuccess);
    }

    // Expected failures are found before the directory opens its own unit of work, so they never doom the caller's.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_call_leaves_the_callers_unit_of_work_able_to_commit(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            var invalid = await directory.IssueAsync(CatalogSync(Start));
            var unknown = await directory.RevokeAsync(Guid.NewGuid());
            Assert.True((await directory.IssueAsync(CatalogSync())).IsSuccess);

            Assert.Equal(AgentCredentialErrorCodes.ExpiryNotInFuture, Assert.Single(invalid.Errors).Code);
            Assert.Equal(AgentCredentialErrorCodes.CredentialNotFound, Assert.Single(unknown.Errors).Code);
            await work.CommitAsync();
        }

        Assert.Single(await scenario.RowsAsync());
        Assert.Single(await scenario.CredentialEventsAsync());
    }

    // The second revoke reads while the first has not committed, so its own check passes and the conditional update decides.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_revokes_one_wins_and_the_other_is_told_it_is_already_revoked(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);
        var id = (await scenario.IssueAsync()).Credential.Id;

        Result<AgentCredential> loser;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().RevokeAsync(id)).IsSuccess);

            var second = Task.Run(() => scenario.RunAsync(directory => directory.RevokeAsync(id)));
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await work.CommitAsync();
            await work.DisposeAsync();

            loser = await second;
        }

        Assert.Equal(AgentCredentialErrorCodes.AlreadyRevoked, Assert.Single(loser.Errors).Code);
        Assert.Single(await scenario.CredentialEventsAsync(), row => row.Operation == "agent_credential.revoked");
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task No_log_line_contains_a_key(string provider)
    {
        await using var scenario = await AgentCredentialScenario.CreateAsync(provider);

        var issued = await scenario.IssueAsync();
        await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key));
        await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key + "A"));
        await scenario.RunAsync(directory => directory.RevokeAsync(issued.Credential.Id));
        await scenario.RunAsync(directory => directory.AuthenticateAsync(issued.Key));

        // The calls were logged down to their SQL, so the absence below is not for want of logging.
        Assert.Contains(scenario.Logs, line => line.Contains("\"AgentCredentials\"", StringComparison.Ordinal));
        Assert.DoesNotContain(scenario.Logs, line => line.Contains(issued.Key[AgentKeys.Prefix.Length..], StringComparison.Ordinal));
    }
}
