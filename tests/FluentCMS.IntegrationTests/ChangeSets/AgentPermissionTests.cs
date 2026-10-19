using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class AgentPermissionTests
{
    // A path from open to each state a transition in the table starts from, each step made by whoever may make it.
    private static readonly Dictionary<ChangeSetState, ChangeSetState[]> PathFromOpen = new()
    {
        [ChangeSetState.Open] = [],
        [ChangeSetState.ChecksRunning] = [ChangeSetState.ChecksRunning],
        [ChangeSetState.ChecksFailed] = [ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed],
        [ChangeSetState.AwaitingReview] = [ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview],
        [ChangeSetState.Approved] = [ChangeSetState.ChecksRunning, ChangeSetState.Approved],
        [ChangeSetState.Published] = [ChangeSetState.ChecksRunning, ChangeSetState.Approved, ChangeSetState.Published],
    };

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_makes_no_move_but_submit_and_reopen_and_each_refusal_is_logged(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var agentMoves = ChangeSetTransitions.All.Where(transition => transition.To is not (ChangeSetState.ChecksRunning or ChangeSetState.Open));

        foreach (var ((from, to), index) in agentMoves.Select((transition, index) => (transition, index)))
        {
            // A new type each time, so a set that reaches published has no earlier publish of its item to be stale against.
            var changeSet = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync($"type-{index}")));
            foreach (var state in PathFromOpen[from])
            {
                Assert.True((await MoveAsync(scenario, changeSet.Id, state)).IsSuccess);
            }

            var before = await scenario.ChangeSetRowAsync(changeSet.Id);
            var events = (await scenario.ChangeSetEventsAsync(changeSet.Id)).Count;

            var refused = await scenario.RunAsync(directory => to == ChangeSetState.Rejected
                ? directory.RejectAsync(changeSet.Id, "The price has no source.")
                : directory.TransitionAsync(changeSet.Id, to));

            var error = Assert.Single(refused.Errors);
            Assert.Equal(AgentPermissionErrorCodes.TransitionNotPermitted, error.Code);
            Assert.Contains(Agent.DisplayName, error.Message);
            Assert.Contains("An agent submits its change set", error.Message);

            var after = await scenario.ChangeSetRowAsync(changeSet.Id);
            Assert.Equal(from, after.State);
            Assert.Equal(before.Revision, after.Revision);
            Assert.Null(after.RejectionReason);

            var logged = await scenario.ChangeSetEventsAsync(changeSet.Id);
            Assert.Equal(events + 1, logged.Count);
            var refusal = logged[^1];
            Assert.Equal("agent.transition_refused", refusal.Operation);
            Assert.Equal("change_set", refusal.SubjectType);
            Assert.Equal(changeSet.Id, refusal.SubjectId);
            Assert.Equal(InitiatorKinds.Agent, refusal.InitiatorKind);
            Assert.Equal(Agent.CredentialId, refusal.AgentCredentialId);
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_submits_its_set_and_reopens_it_after_failed_checks(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var changeSet = await scenario.OpenAsync(Update(product));

        var submitted = await scenario.RunAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.ChecksRunning));
        await MoveAsync(scenario, changeSet.Id, ChangeSetState.ChecksFailed);
        var reopened = await scenario.RunAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.Open));

        Assert.True(submitted.IsSuccess);
        Assert.True(reopened.IsSuccess);
        Assert.Equal(ChangeSetState.Open, (await scenario.ChangeSetRowAsync(changeSet.Id)).State);

        var events = await scenario.ChangeSetEventsAsync(changeSet.Id);
        Assert.DoesNotContain(events, row => row.Operation.StartsWith("agent.", StringComparison.Ordinal));
        Assert.All(
            events.Where(row => row.Operation is "change_set.submitted" or "change_set.reopened"),
            row => Assert.Equal(Agent.CredentialId, row.AgentCredentialId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_submits_reopens_and_adds_to_its_own_change_sets_only(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var persons = (await scenario.AsPersonAsync(directory => directory.OpenAsync(CatalogSync))).Value;
        persons = (await scenario.AsPersonAsync(directory => directory.AddOperationAsync(persons.Id, Update(product)))).Value;
        var before = await scenario.ChangeSetRowAsync(persons.Id);

        var submitted = await scenario.RunAsync(directory => directory.TransitionAsync(persons.Id, ChangeSetState.ChecksRunning));
        var added = await scenario.RunAsync(directory => directory.AddOperationAsync(persons.Id, Create(category)));

        foreach (var refused in new[] { submitted, added })
        {
            var error = Assert.Single(refused.Errors);
            Assert.Equal(AgentPermissionErrorCodes.NotOwner, error.Code);
            Assert.Contains(Amir.DisplayName, error.Message);
        }

        var after = await scenario.ChangeSetRowAsync(persons.Id);
        Assert.Equal((ChangeSetState.Open, before.Revision), (after.State, after.Revision));
        Assert.Single(await scenario.OperationRowsAsync(persons.Id));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_operation_past_the_agents_items_cap_is_refused_naming_the_cap(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var region = await scenario.ContentTypeAsync("region");
        await scenario.UpdateAgentAsync(row => row.ItemsPerChangeSet = 2);

        var changeSet = await scenario.OpenAsync(Create(category), Update(product));
        var refused = await scenario.RunAsync(directory => directory.AddOperationAsync(changeSet.Id, Create(region)));

        var error = Assert.Single(refused.Errors);
        Assert.Equal(AgentPermissionErrorCodes.CapExceeded, error.Code);
        Assert.Contains("items per change set", error.Message);
        Assert.Contains("allows 2", error.Message);
        Assert.Equal(2, (await scenario.OperationRowsAsync(changeSet.Id)).Count);

        var refusal = (await scenario.ChangeSetEventsAsync(changeSet.Id))[^1];
        Assert.Equal("agent.items_per_change_set_exceeded", refusal.Operation);
        Assert.Equal(changeSet.Id, refusal.SubjectId);

        // Joining a session's set meets the same cap, and a person is held to none of it.
        await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)));
        await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Update(product)));
        var third = await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(region)));
        var person = await scenario.AsPersonAsync(directory => directory.AddOperationAsync(changeSet.Id, Create(region)));

        Assert.Equal(AgentPermissionErrorCodes.CapExceeded, Assert.Single(third.Errors).Code);
        Assert.True(person.IsSuccess);
        Assert.Equal(3, (await scenario.OperationRowsAsync(changeSet.Id)).Count);
    }

    // The second writer reads while the first has not committed, so the cap passes for both and the revision has the last word.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_operations_that_would_each_reach_the_cap_one_is_stored(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var region = await scenario.ContentTypeAsync("region");
        await scenario.UpdateAgentAsync(row => row.ItemsPerChangeSet = 2);
        var changeSet = await scenario.OpenAsync(Create(category));

        Result<ChangeSet> lost;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>().AddOperationAsync(changeSet.Id, Update(product))).IsSuccess);

            var second = Task.Run(() => scenario.RunAsync(directory => directory.AddOperationAsync(changeSet.Id, Create(region))));
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await work.CommitAsync();
            await work.DisposeAsync();
            lost = await second;
        }

        Assert.False(lost.IsSuccess);
        Assert.Equal(2, (await scenario.OperationRowsAsync(changeSet.Id)).Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_change_set_past_the_agents_daily_cap_is_refused_until_the_day_has_passed(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        await scenario.UpdateAgentAsync(row => row.ChangeSetsPerDay = 1);

        var first = await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)));
        var joined = await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Update(product)));
        var opened = await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync));
        var otherSession = await scenario.RunAsync(directory => directory.AddToSessionAsync("another-session", CatalogSync, Create(category)));
        var person = await scenario.AsPersonAsync(directory => directory.OpenAsync(CatalogSync));

        Assert.True(first.IsSuccess);
        Assert.True(joined.IsSuccess);
        Assert.True(person.IsSuccess);
        foreach (var refused in new[] { opened, otherSession })
        {
            var error = Assert.Single(refused.Errors);
            Assert.Equal(AgentPermissionErrorCodes.CapExceeded, error.Code);
            Assert.Contains("change sets per day", error.Message);
            Assert.Contains("its cap is 1", error.Message);
        }

        Assert.Equal(2, (await scenario.ChangeSetRowsAsync()).Count);
        var refusals = await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.Operation == "agent.change_sets_per_day_exceeded")
            .ToListAsync());
        Assert.Equal(2, refusals.Count);
        Assert.All(refusals, row => Assert.Equal(("agent_credential", Agent.CredentialId), (row.SubjectType, row.SubjectId!.Value)));

        scenario.AdvanceClock(TimeSpan.FromDays(1));

        Assert.True((await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync))).IsSuccess);
    }

    // Each open holds the agent's credential before it counts, so concurrent opens take turns and only the cap's worth are stored.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_ten_concurrent_opens_by_an_agent_with_a_daily_cap_of_two_two_are_stored(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        await scenario.UpdateAgentAsync(row => row.ChangeSetsPerDay = 2);

        var opens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => scenario.RunAsync(directory => directory.OpenAsync(CatalogSync)))));

        Assert.Equal(2, opens.Count(open => open.IsSuccess));
        Assert.Equal(
            Enumerable.Repeat(AgentPermissionErrorCodes.CapExceeded, 8),
            opens.Where(open => !open.IsSuccess).Select(open => Assert.Single(open.Errors).Code));
        Assert.Equal(2, (await scenario.ChangeSetRowsAsync()).Count);
        Assert.Equal(8, await scenario.QueryAsync(context => context.AuditEvents.CountAsync(row => row.Operation == "agent.change_sets_per_day_exceeded")));
    }

    // The refusal is a security event (SEC-08), so it outlives the caller's unit of work, which the refusal rolls back.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_cap_refused_inside_a_unit_of_work_that_rolls_back_keeps_its_event(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        await scenario.UpdateAgentAsync(row => row.ChangeSetsPerDay = 1);

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            var directory = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();

            Assert.True((await directory.OpenAsync(CatalogSync)).IsSuccess);
            Assert.Equal(AgentPermissionErrorCodes.CapExceeded, Assert.Single((await directory.OpenAsync(CatalogSync)).Errors).Code);
            await work.DisposeAsync();
        }

        Assert.Empty(await scenario.ChangeSetRowsAsync());
        var refusal = Assert.Single(await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.Operation == "agent.change_sets_per_day_exceeded")
            .ToListAsync()));
        Assert.Equal(("agent_credential", Agent.CredentialId), (refusal.SubjectType, refusal.SubjectId!.Value));
        Assert.Equal(Agent.CredentialId, refusal.AgentCredentialId);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_its_own_change_sets_only_and_a_person_reads_every_set(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var own = (await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync))).Value;
        var persons = (await scenario.AsPersonAsync(directory => directory.OpenAsync(CatalogSync))).Value;
        var other = new AgentInitiator(Guid.NewGuid(), "other-agent");
        await scenario.AddAgentAsync(other);
        scenario.Initiator = other;
        var others = (await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync))).Value;
        scenario.Initiator = Agent;

        Assert.True((await scenario.RunAsync(directory => directory.GetAsync(own.Id))).IsSuccess);
        Assert.True((await scenario.RunAsync(directory => directory.DiffAsync(own.Id))).IsSuccess);
        foreach (var id in new[] { persons.Id, others.Id })
        {
            var read = await scenario.RunAsync(directory => directory.GetAsync(id));
            var diff = await scenario.RunAsync(directory => directory.DiffAsync(id));

            Assert.Equal(AgentPermissionErrorCodes.NotOwner, Assert.Single(read.Errors).Code);
            Assert.Equal(AgentPermissionErrorCodes.NotOwner, Assert.Single(diff.Errors).Code);
        }

        Assert.True((await scenario.AsPersonAsync(directory => directory.GetAsync(others.Id))).IsSuccess);
        Assert.True((await scenario.AsPersonAsync(directory => directory.DiffAsync(own.Id))).IsSuccess);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_without_an_active_credential_for_the_site_writes_nothing(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var changeSet = await scenario.OpenAsync(Update(product));
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        await scenario.UpdateAgentAsync(row => row.Sites = [Guid.NewGuid()]);
        var outsideSite = await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync));

        await scenario.UpdateAgentAsync(row => row.Sites = [Site.Default.Id]);
        await scenario.UpdateAgentAsync(row => row.ExpiresAt = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
        var expired = await scenario.RunAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.ChecksRunning));

        await scenario.UpdateAgentAsync(row => row.ExpiresAt = new DateTimeOffset(2027, 10, 1, 0, 0, 0, TimeSpan.Zero));
        await scenario.UpdateAgentAsync(row => row.RevokedAt = new DateTimeOffset(2026, 10, 18, 0, 0, 0, TimeSpan.Zero));
        var revoked = await scenario.RunAsync(directory => directory.AddOperationAsync(changeSet.Id, Create(category)));

        scenario.Initiator = new AgentInitiator(Guid.NewGuid(), "unknown-agent");
        var unknown = await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync));

        Assert.Equal(AgentPermissionErrorCodes.SiteOutOfScope, Assert.Single(outsideSite.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.Expired, Assert.Single(expired.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.Revoked, Assert.Single(revoked.Errors).Code);
        Assert.Equal(AgentCredentialErrorCodes.CredentialNotFound, Assert.Single(unknown.Errors).Code);

        Assert.Single(await scenario.ChangeSetRowsAsync());
        Assert.Single(await scenario.OperationRowsAsync(changeSet.Id));
        Assert.Equal(ChangeSetState.Open, (await scenario.ChangeSetRowAsync(changeSet.Id)).State);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
    }

    // Submitting and reopening are the agent's own moves; the others are made as a person, as the checks and the desk will.
    private static Task<Result<ChangeSet>> MoveAsync(ChangeSetScenario scenario, Guid id, ChangeSetState state)
    {
        return state is ChangeSetState.ChecksRunning or ChangeSetState.Open
            ? scenario.RunAsync(directory => directory.TransitionAsync(id, state))
            : scenario.AsPersonAsync(directory => directory.TransitionAsync(id, state));
    }
}
