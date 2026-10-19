using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

public sealed class ChangeSetDirectoryTests
{
    // The audit operation of each transition, by the state it reaches.
    private static readonly Dictionary<ChangeSetState, string> TransitionEvents = new()
    {
        [ChangeSetState.Open] = "change_set.reopened",
        [ChangeSetState.ChecksRunning] = "change_set.submitted",
        [ChangeSetState.ChecksFailed] = "change_set.checks_failed",
        [ChangeSetState.AwaitingReview] = "change_set.awaiting_review",
        [ChangeSetState.Approved] = "change_set.approved",
        [ChangeSetState.Published] = "change_set.published",
        [ChangeSetState.Rejected] = "change_set.rejected",
        [ChangeSetState.Reverted] = "change_set.reverted",
    };

    // A path from open to each state a transition in the table starts from.
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
    public async Task An_opened_set_reads_back_and_its_opening_is_audited(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);

        var opened = (await scenario.RunAsync(directory => directory.OpenAsync(CatalogSync))).Value;
        var read = (await scenario.RunAsync(directory => directory.GetAsync(opened.Id))).Value;

        Assert.Equal(opened.Id, read.Id);
        Assert.Equal(Site.Default.Id, read.SiteId);
        Assert.Equal(CatalogSync.Rationale, read.Rationale);
        Assert.Equal(CatalogSync.Sources, read.Sources);
        Assert.Equal(Agent, read.Initiator);
        Assert.Null(read.SessionId);
        Assert.Equal(ChangeSetState.Open, read.State);
        Assert.Empty(read.Operations);

        var opening = Assert.Single(await scenario.ChangeSetEventsAsync());
        Assert.Equal("change_set.opened", opening.Operation);
        Assert.Equal("change_set", opening.SubjectType);
        Assert.Equal(opened.Id, opening.SubjectId);
        Assert.Equal(opened.Id, opening.ChangeSetId);
        Assert.Equal(InitiatorKinds.Agent, opening.InitiatorKind);
        Assert.Equal(Agent.CredentialId, opening.AgentCredentialId);
        Assert.Equal(CatalogSync.Sources, opening.Sources);

        var missing = await scenario.RunAsync(directory => directory.GetAsync(Guid.NewGuid()));
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(missing.Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Schema_operations_point_at_stored_content_type_versions_and_are_audited(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var productVersions = await scenario.QueryAsync(context => context.ContentTypeVersions
            .AsNoTracking()
            .Where(version => version.ContentTypeId == product.Id)
            .OrderBy(version => version.Version)
            .ToListAsync());

        var changeSet = await scenario.OpenAsync(Create(category), Update(product));
        var read = (await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value;

        Assert.Equal([Create(category), Update(product)], read.Operations);
        Assert.Equal([0, 1], (await scenario.OperationRowsAsync(changeSet.Id)).Select(row => row.Position));

        var added = (await scenario.ChangeSetEventsAsync()).Where(row => row.Operation == "change_set.operation_added").ToList();
        Assert.Equal(2, added.Count);
        Assert.All(added, row => Assert.Equal("content_type", row.SubjectType));
        Assert.All(added, row => Assert.Equal(changeSet.Id, row.ChangeSetId));
        Assert.All(added, row => Assert.Equal(CatalogSync.Sources, row.Sources));
        Assert.Equal([category.Id, product.Id], added.Select(row => row.SubjectId));
        Assert.Equal([null, productVersions[0].Id], added.Select(row => row.PreviousVersionId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_operation_that_does_not_point_at_stored_versions_fails_without_writing(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var category = await scenario.ContentTypeAsync("category");
        var changeSet = await scenario.OpenAsync(Update(product));
        var revision = (await scenario.ChangeSetRowAsync(changeSet.Id)).Revision;

        (ChangeOperation Operation, string[] Codes)[] cases =
        [
            (ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, Guid.NewGuid(), 1, 2).Value, [ChangeSetErrorCodes.ItemNotFound]),
            (
                ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, category.Id, 3, 5).Value,
                [ChangeSetErrorCodes.VersionNotFound, ChangeSetErrorCodes.VersionNotFound]
            ),
            // Only a person puts a rule in a set, so an agent's rule operation is refused before anything is read.
            (ChangeOperation.Create(OperationKind.Update, ItemKind.Rule, Guid.NewGuid(), 1, 2).Value, [RuleErrorCodes.PeopleOnly]),
            (ChangeOperation.Create(OperationKind.Delete, ItemKind.Schema, product.Id, 2, null).Value, [ChangeSetErrorCodes.ItemDuplicate]),
        ];

        foreach (var (operation, codes) in cases)
        {
            var result = await scenario.RunAsync(directory => directory.AddOperationAsync(changeSet.Id, operation));
            Assert.Equal(codes, result.Errors.Select(error => error.Code));
        }

        var unknownSet = await scenario.RunAsync(directory => directory.AddOperationAsync(Guid.NewGuid(), Update(product)));
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknownSet.Errors).Code);

        Assert.Single(await scenario.OperationRowsAsync(changeSet.Id));
        Assert.Equal(revision, (await scenario.ChangeSetRowAsync(changeSet.Id)).Revision);
        Assert.Equal(["change_set.opened", "change_set.operation_added"], (await scenario.ChangeSetEventsAsync()).Select(row => row.Operation));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_transition_in_the_table_is_stored_with_its_event(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);

        foreach (var ((from, to), index) in ChangeSetTransitions.All.Select((transition, index) => (transition, index)))
        {
            // A new type each time, so a set that reaches published has no earlier publish of its item to be stale against.
            var changeSet = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync($"type-{index}")));
            foreach (var state in PathFromOpen[from])
            {
                Assert.True((await MoveAsync(scenario, changeSet.Id, state)).IsSuccess);
            }

            scenario.Initiator = Amir;
            var moved = await MoveAsync(scenario, changeSet.Id, to);
            scenario.Initiator = Agent;

            Assert.True(moved.IsSuccess, $"{from} to {to}: {string.Join("; ", moved.Errors.Select(error => error.Message))}");
            Assert.Equal(to, moved.Value.State);

            var stored = await scenario.ChangeSetRowAsync(changeSet.Id);
            Assert.Equal(to, stored.State);
            Assert.Equal(to == ChangeSetState.Rejected ? "The price has no source." : null, stored.RejectionReason);
            Assert.Equal(to, (await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.State);

            var last = (await scenario.ChangeSetEventsAsync(changeSet.Id))[^1];
            Assert.Equal(TransitionEvents[to], last.Operation);
            Assert.Equal("change_set", last.SubjectType);
            Assert.Equal(changeSet.Id, last.SubjectId);
            Assert.Equal(InitiatorKinds.User, last.InitiatorKind);
            Assert.Equal(Amir.UserId, last.InitiatorId);
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_transition_writes_nothing(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var empty = await scenario.OpenAsync();
        var reviewed = await scenario.OpenAsync(Update(product));
        await MoveAsync(scenario, reviewed.Id, ChangeSetState.ChecksRunning);
        await MoveAsync(scenario, reviewed.Id, ChangeSetState.AwaitingReview);
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        var withoutOperations = await scenario.RunAsync(directory => directory.TransitionAsync(empty.Id, ChangeSetState.ChecksRunning));
        var skipped = await scenario.AsPersonAsync(directory => directory.TransitionAsync(reviewed.Id, ChangeSetState.Published));
        var silentRejection = await scenario.AsPersonAsync(directory => directory.TransitionAsync(reviewed.Id, ChangeSetState.Rejected));
        var blankReason = await scenario.AsPersonAsync(directory => directory.RejectAsync(reviewed.Id, "  "));
        var unknown = await scenario.RunAsync(directory => directory.TransitionAsync(Guid.NewGuid(), ChangeSetState.ChecksRunning));

        Assert.Equal(ChangeSetErrorCodes.OperationsMissing, Assert.Single(withoutOperations.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(skipped.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.RejectionReasonMissing, Assert.Single(silentRejection.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.RejectionReasonMissing, Assert.Single(blankReason.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(ChangeSetState.Open, (await scenario.ChangeSetRowAsync(empty.Id)).State);
        Assert.Equal(ChangeSetState.AwaitingReview, (await scenario.ChangeSetRowAsync(reviewed.Id)).State);
        Assert.Null((await scenario.ChangeSetRowAsync(reviewed.Id)).RejectionReason);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
    }

    // The audit log refuses to append without an initiator, after the state update has already run in the transaction.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failure_mid_transition_leaves_neither_the_state_change_nor_its_event(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var changeSet = await scenario.OpenAsync(Update(product));
        await MoveAsync(scenario, changeSet.Id, ChangeSetState.ChecksRunning);
        var before = await scenario.ChangeSetRowAsync(changeSet.Id);
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        scenario.Initiator = null;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.RunAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.AwaitingReview)));

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>().TransitionAsync(changeSet.Id, ChangeSetState.AwaitingReview));

            // Inside the still open transaction the update is visible, so the failure came after it reached the database.
            Assert.Equal(
                ChangeSetState.AwaitingReview,
                await context.ChangeSets.AsNoTracking().Where(row => row.Id == changeSet.Id).Select(row => row.State).SingleAsync());
        }

        scenario.Initiator = Agent;
        var after = await scenario.ChangeSetRowAsync(changeSet.Id);
        Assert.Equal(ChangeSetState.ChecksRunning, after.State);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);
        Assert.True((await scenario.AsPersonAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.AwaitingReview))).IsSuccess);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_sessions_operations_share_a_set_until_it_leaves_open(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var region = await scenario.ContentTypeAsync("region");
        var later = new NewChangeSet("Ignored once the set is open.", ["exports/catalog-2026-10-18.csv", "https://example.com/spec-sheet"]);

        var first = (await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)))).Value;
        var second = (await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", later, Update(product)))).Value;

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("example-session", second.SessionId);
        Assert.Equal(CatalogSync.Rationale, second.Rationale);
        Assert.Equal(["exports/catalog-2026-10-18.csv", "https://example.com/spec-sheet"], second.Sources);
        Assert.Equal([Create(category), Update(product)], (await scenario.RunAsync(directory => directory.GetAsync(first.Id))).Value.Operations);
        Assert.Equal(
            ["change_set.opened", "change_set.operation_added", "change_set.operation_added"],
            (await scenario.ChangeSetEventsAsync(first.Id)).Select(row => row.Operation));

        var otherSession = (await scenario.RunAsync(directory => directory.AddToSessionAsync("another-session", CatalogSync, Create(category)))).Value;
        scenario.Initiator = Amir;
        var otherInitiator = (await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)))).Value;
        scenario.Initiator = Agent;

        Assert.NotEqual(first.Id, otherSession.Id);
        Assert.NotEqual(first.Id, otherInitiator.Id);
        Assert.Equal(Amir, otherInitiator.Initiator);

        await MoveAsync(scenario, first.Id, ChangeSetState.ChecksRunning);
        var next = (await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(region)))).Value;

        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal([Create(region)], next.Operations);
        Assert.Equal(2, (await scenario.RunAsync(directory => directory.GetAsync(first.Id))).Value.Operations.Count);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_session_has_at_most_one_open_set(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");
        var first = (await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)))).Value;
        await MoveAsync(scenario, first.Id, ChangeSetState.ChecksRunning);
        await MoveAsync(scenario, first.Id, ChangeSetState.ChecksFailed);
        var second = (await scenario.RunAsync(directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)))).Value;
        var events = (await scenario.ChangeSetEventsAsync()).Count;

        var reopened = await scenario.RunAsync(directory => directory.TransitionAsync(first.Id, ChangeSetState.Open));

        Assert.Equal(ChangeSetErrorCodes.SessionHasOpenSet, Assert.Single(reopened.Errors).Code);
        Assert.Contains(second.Id.ToString(), reopened.Errors[0].Message);
        Assert.Equal(ChangeSetState.ChecksFailed, (await scenario.ChangeSetRowAsync(first.Id)).State);
        Assert.Equal(events, (await scenario.ChangeSetEventsAsync()).Count);

        await MoveAsync(scenario, second.Id, ChangeSetState.ChecksRunning);
        Assert.True((await scenario.RunAsync(directory => directory.TransitionAsync(first.Id, ChangeSetState.Open))).IsSuccess);

        var invalid = await scenario.RunAsync(directory => directory.AddToSessionAsync(" ", CatalogSync, Create(category)));
        Assert.Equal(ChangeSetErrorCodes.SessionIdInvalid, Assert.Single(invalid.Errors).Code);
    }

    // Expected failures are found before the directory opens its own unit of work, so they never doom the caller's.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failed_operation_leaves_the_callers_unit_of_work_able_to_commit(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        Guid id;

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>();
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

            id = (await directory.OpenAsync(CatalogSync)).Value.Id;
            var unknown = await directory.AddOperationAsync(id, ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, Guid.NewGuid(), 1, 2).Value);
            Assert.True((await directory.AddOperationAsync(id, Update(product))).IsSuccess);
            scenario.Initiator = Amir;
            var refused = await directory.TransitionAsync(id, ChangeSetState.Published);

            Assert.Equal(ChangeSetErrorCodes.ItemNotFound, Assert.Single(unknown.Errors).Code);
            Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(refused.Errors).Code);
            await work.CommitAsync();
        }

        Assert.Single(await scenario.OperationRowsAsync(id));
        Assert.Equal(["change_set.opened", "change_set.operation_added"], (await scenario.ChangeSetEventsAsync()).Select(row => row.Operation));
    }

    // The second writer reads while the first has not committed, so its own checks pass and the revision has the last word.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_transitions_one_wins_and_the_other_gets_a_conflict(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var changeSet = await scenario.OpenAsync(Update(product));
        await MoveAsync(scenario, changeSet.Id, ChangeSetState.ChecksRunning);
        scenario.Initiator = Amir;

        var lost = await scenario.RaceAsync(
            directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.AwaitingReview),
            directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.ChecksFailed));

        Assert.Equal(ChangeSetErrorCodes.Conflict, Assert.Single(lost.Errors).Code);
        Assert.Equal(ChangeSetState.AwaitingReview, (await scenario.ChangeSetRowAsync(changeSet.Id)).State);
        Assert.DoesNotContain("change_set.checks_failed", (await scenario.ChangeSetEventsAsync()).Select(row => row.Operation));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Of_two_concurrent_first_operations_in_a_session_one_opens_the_set_and_the_other_gets_a_conflict(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var category = await scenario.ContentTypeAsync("category");

        var lost = await scenario.RaceAsync(
            directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)),
            directory => directory.AddToSessionAsync("example-session", CatalogSync, Create(category)));

        Assert.Equal(ChangeSetErrorCodes.Conflict, Assert.Single(lost.Errors).Code);
        var stored = Assert.Single(await scenario.ChangeSetRowsAsync());
        Assert.Single(await scenario.OperationRowsAsync(stored.Id));
        Assert.Equal(["change_set.opened", "change_set.operation_added"], (await scenario.ChangeSetEventsAsync()).Select(row => row.Operation));
    }

    // Submitting and reopening are the caller's own moves; the others are made as a person, as the checks and the desk will.
    private static async Task<Result<ChangeSet>> MoveAsync(ChangeSetScenario scenario, Guid id, ChangeSetState state)
    {
        // A set is reverted by publishing its revert, which moves it in the same unit of work; a person makes every step.
        if (state == ChangeSetState.Reverted)
        {
            var caller = scenario.Initiator;
            scenario.Initiator = Amir;
            try
            {
                var revert = (await scenario.RunAsync(directory => directory.RevertAsync(id, "The sync used the wrong export."))).Value;
                await scenario.PublishAsync(revert.Id);
                return await scenario.RunAsync(directory => directory.GetAsync(id));
            }
            finally
            {
                scenario.Initiator = caller;
            }
        }

        Func<IChangeSetDirectory, Task<Result<ChangeSet>>> move = directory => state == ChangeSetState.Rejected
            ? directory.RejectAsync(id, "The price has no source.")
            : directory.TransitionAsync(id, state);

        return state is ChangeSetState.ChecksRunning or ChangeSetState.Open ? await scenario.RunAsync(move) : await scenario.AsPersonAsync(move);
    }
}
