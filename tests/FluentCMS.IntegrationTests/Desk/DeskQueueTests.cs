using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using FluentCMS.IntegrationTests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.Desk;

public sealed class DeskQueueTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Each_queue_lists_its_sets_in_its_order_and_counts_them(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var old = await scenario.PublishAsync(Create(await scenario.ContentTypeAsync("old")));
        scenario.AdvanceClock(TimeSpan.FromDays(8));

        var published = await scenario.PublishAsync(Create(await scenario.ContentTypeAsync("product")));
        var undone = await scenario.PublishAsync(Create(await scenario.ContentTypeAsync("category")));
        var revert = await RevertAsync(scenario, undone.Id);
        await MoveAsPersonAsync(scenario, revert.Id, ChangeSetState.ChecksRunning, ChangeSetState.Approved, ChangeSetState.Published);
        var brand = await scenario.PublishAsync(Create(await scenario.ContentTypeAsync("brand")));
        var waiting = await AwaitingReviewAsync(scenario, "article");
        var failed = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync("page")));
        await SubmitAsync(scenario, failed.Id);
        await MoveAsPersonAsync(scenario, failed.Id, ChangeSetState.ChecksFailed);
        var revertWaiting = await RevertAsync(scenario, brand.Id);
        await MoveAsPersonAsync(scenario, revertWaiting.Id, ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview);
        await scenario.OpenAsync(Create(await scenario.ContentTypeAsync("draft")));
        await SubmitAsync(scenario, (await scenario.OpenAsync(Create(await scenario.ContentTypeAsync("running")))).Id);

        var counts = (await QueuesAsync(scenario, queues => queues.CountAsync())).Value;

        Assert.Equal(
            [
                (DeskQueue.AwaitingMe, 2),
                (DeskQueue.AwaitingAnyone, 2),
                (DeskQueue.ChecksFailed, 1),
                (DeskQueue.StaleFacts, 0),
                (DeskQueue.RecentlyPublished, 3),
                (DeskQueue.Reverted, 1),
            ],
            counts.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));

        Assert.Equal([waiting.Id, revertWaiting.Id], await IdsAsync(scenario, DeskQueue.AwaitingMe));
        Assert.Equal([waiting.Id, revertWaiting.Id], await IdsAsync(scenario, DeskQueue.AwaitingAnyone));
        Assert.Equal([failed.Id], await IdsAsync(scenario, DeskQueue.ChecksFailed));
        Assert.Equal([brand.Id, revert.Id, published.Id], await IdsAsync(scenario, DeskQueue.RecentlyPublished));
        Assert.Equal([undone.Id], await IdsAsync(scenario, DeskQueue.Reverted));
        Assert.DoesNotContain(old.Id, await IdsAsync(scenario, DeskQueue.RecentlyPublished));

        var awaiting = (await QueuesAsync(scenario, queues => queues.ListChangeSetsAsync(DeskQueue.AwaitingAnyone, 0, 50))).Value.Items;
        Assert.Equal(((Guid?)null, (Initiator)Agent, ChangeSetState.AwaitingReview, 1), Describe(awaiting[0]));
        Assert.Equal(((Guid?)brand.Id, (Initiator)Amir, ChangeSetState.AwaitingReview, 1), Describe(awaiting[1]));
        Assert.Equal(CatalogSync.Rationale, awaiting[0].Rationale);
        Assert.All(awaiting, set => Assert.True(set.OpenedAt < set.StateSince));
        Assert.True(awaiting[0].StateSince < awaiting[1].StateSince);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_page_holds_its_window_of_the_list_and_the_total(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var first = await AwaitingReviewAsync(scenario, "one");
        var second = await AwaitingReviewAsync(scenario, "two");
        var third = await AwaitingReviewAsync(scenario, "three");

        var page = await ListAsync(scenario, new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview], Limit = 2 });
        var last = await ListAsync(scenario, new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview], Offset = 2, Limit = 2 });
        var newest = await ListAsync(scenario, new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview], NewestFirst = true, Limit = 1 });
        var beyond = await ListAsync(scenario, new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview], Offset = 5 });

        Assert.Equal([first.Id, second.Id], page.Items.Select(set => set.Id));
        Assert.Equal([third.Id], last.Items.Select(set => set.Id));
        Assert.Equal([third.Id], newest.Items.Select(set => set.Id));
        Assert.Empty(beyond.Items);
        Assert.All([page, last, newest, beyond], list => Assert.Equal(3, list.Total));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_sets_that_entered_their_state_in_time_are_listed(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var before = await AwaitingReviewAsync(scenario, "before");
        scenario.AdvanceClock(TimeSpan.FromHours(1));
        var cutoff = new DateTimeOffset(2026, 10, 18, 12, 30, 0, TimeSpan.Zero);
        var after = await AwaitingReviewAsync(scenario, "after");

        // A refused move appends an event about the set, and the set has still not moved.
        var refused = await scenario.RunAsync(directory => directory.TransitionAsync(before.Id, ChangeSetState.Approved));
        Assert.False(refused.IsSuccess);

        var listed = await ListAsync(scenario, new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview], EnteredSince = cutoff });
        await using var scope = scenario.Services.CreateAsyncScope();
        var counted = await scope.ServiceProvider.GetRequiredService<IChangeSetLists>()
            .CountAsync(new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview], EnteredSince = cutoff.ToOffset(TimeSpan.FromHours(2)) });

        Assert.Equal([after.Id], listed.Items.Select(set => set.Id));
        Assert.Equal(1, listed.Total);
        Assert.Equal(1, counted.Value);
        Assert.Equal(2, (await ListAsync(scenario, new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview] })).Total);
        Assert.NotEqual(before.Id, after.Id);
    }

    [Theory]
    [InlineData(-1, 50, ChangeSetErrorCodes.OffsetInvalid)]
    [InlineData(0, 0, ChangeSetErrorCodes.LimitInvalid)]
    [InlineData(0, 201, ChangeSetErrorCodes.LimitInvalid)]
    public async Task A_window_out_of_range_is_refused(int offset, int limit, string code)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(DatabaseProviders.Sqlite);

        var sets = await QueuesAsync(scenario, queues => queues.ListChangeSetsAsync(DeskQueue.AwaitingAnyone, offset, limit));
        var facts = await QueuesAsync(scenario, queues => queues.ListStaleFactsAsync(offset, limit));

        Assert.Equal(code, Assert.Single(sets.Errors).Code);
        Assert.Equal(code, Assert.Single(facts.Errors).Code);
    }

    [Fact]
    public async Task A_list_without_states_is_refused()
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(DatabaseProviders.Sqlite);
        await using var scope = scenario.Services.CreateAsyncScope();
        var lists = scope.ServiceProvider.GetRequiredService<IChangeSetLists>();

        var listed = await lists.ListAsync(new ChangeSetListQuery { States = [] });
        var counted = await lists.CountAsync(new ChangeSetListQuery { States = [] });

        Assert.Equal(ChangeSetErrorCodes.ListStatesMissing, Assert.Single(listed.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.ListStatesMissing, Assert.Single(counted.Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Reading_the_queues_appends_no_event(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        await AwaitingReviewAsync(scenario, "product");
        var events = await scenario.QueryAsync(context => context.AuditEvents.CountAsync());

        Assert.True((await QueuesAsync(scenario, queues => queues.CountAsync())).IsSuccess);
        foreach (var queue in Enum.GetValues<DeskQueue>().Where(queue => queue != DeskQueue.StaleFacts))
        {
            Assert.True((await QueuesAsync(scenario, queues => queues.ListChangeSetsAsync(queue, 0, 50))).IsSuccess);
        }

        Assert.True((await QueuesAsync(scenario, queues => queues.ListStaleFactsAsync(0, 50))).IsSuccess);
        Assert.Equal(events, await scenario.QueryAsync(context => context.AuditEvents.CountAsync()));
        Assert.Equal(
            DeskErrorCodes.QueueNotChangeSets,
            Assert.Single((await QueuesAsync(scenario, queues => queues.ListChangeSetsAsync(DeskQueue.StaleFacts, 0, 50))).Errors).Code);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_stale_facts_queue_lists_expired_and_unverified_facts_the_longest_stale_first(string provider)
    {
        await using var scenario = await EntryScenario.CreateAsync(provider);
        await scenario.Schema.CreateReferenceModelAsync();
        var unverified = await PricedAsync(scenario, validUntil: "null", lastVerified: "\"2020-01-01T00:00:00Z\"");
        var expired = await PricedAsync(scenario, validUntil: "\"2019-06-01T00:00:00Z\"", lastVerified: "null");
        await PricedAsync(scenario, validUntil: "null", lastVerified: "null");

        await using var scope = scenario.Services.CreateAsyncScope();
        var queues = scope.ServiceProvider.GetRequiredService<IDeskQueues>();
        var count = (await queues.CountAsync()).Value[DeskQueue.StaleFacts];
        var first = (await queues.ListStaleFactsAsync(0, 1)).Value;
        var second = (await queues.ListStaleFactsAsync(1, 1)).Value;

        Assert.Equal(2, count);
        Assert.Equal((expired, StaleReason.Expired, 2), (Assert.Single(first.Items).EntityId, first.Items[0].Reason, first.Total));
        Assert.Equal((unverified, StaleReason.Unverified, 2), (Assert.Single(second.Items).EntityId, second.Items[0].Reason, second.Total));
    }

    // The read sides date and describe sets by these move names, so each move the directory makes must append one of them.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_move_the_directory_makes_appends_a_known_move_that_names_the_state_it_leaves(string provider)
    {
        await using var scenario = await ChangeSetScenario.CreateAsync(provider);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var set = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync("product")));
        await ExpectMoveAsync(scenario, set.Id, ChangeSetState.Open, seen, before: []);

        async Task MoveAsync(Guid id, ChangeSetState state, Func<IChangeSetDirectory, Task<FluentCMS.Core.Results.Result<ChangeSet>>> move, bool asPerson = true)
        {
            var before = await MoveEventsAsync(scenario, id);
            var moved = asPerson ? await scenario.AsPersonAsync(move) : await scenario.RunAsync(move);
            Assert.True(moved.IsSuccess, string.Join("; ", moved.Errors.Select(error => error.Message)));
            await ExpectMoveAsync(scenario, id, state, seen, before);
        }

        await MoveAsync(set.Id, ChangeSetState.ChecksRunning, directory => directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning), asPerson: false);
        await MoveAsync(set.Id, ChangeSetState.ChecksFailed, directory => directory.TransitionAsync(set.Id, ChangeSetState.ChecksFailed));
        await MoveAsync(set.Id, ChangeSetState.Open, directory => directory.TransitionAsync(set.Id, ChangeSetState.Open), asPerson: false);
        await MoveAsync(set.Id, ChangeSetState.ChecksRunning, directory => directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning), asPerson: false);
        await MoveAsync(set.Id, ChangeSetState.AwaitingReview, directory => directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview));
        await MoveAsync(set.Id, ChangeSetState.Approved, directory => directory.TransitionAsync(set.Id, ChangeSetState.Approved));
        await MoveAsync(set.Id, ChangeSetState.Published, directory => directory.PublishAsync(set.Id));

        var revert = (await scenario.AsPersonAsync(directory => directory.RevertAsync(set.Id, "Undo it."))).Value;
        await MoveAsync(revert.Id, ChangeSetState.ChecksRunning, directory => directory.TransitionAsync(revert.Id, ChangeSetState.ChecksRunning));
        await MoveAsync(revert.Id, ChangeSetState.AwaitingReview, directory => directory.TransitionAsync(revert.Id, ChangeSetState.AwaitingReview));
        await MoveAsync(revert.Id, ChangeSetState.Approved, directory => directory.TransitionAsync(revert.Id, ChangeSetState.Approved));
        var originalBefore = await MoveEventsAsync(scenario, set.Id);
        await MoveAsync(revert.Id, ChangeSetState.Published, directory => directory.PublishAsync(revert.Id));
        await ExpectMoveAsync(scenario, set.Id, ChangeSetState.Reverted, seen, originalBefore);

        var rejected = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync("category")));
        await MoveAsync(rejected.Id, ChangeSetState.ChecksRunning, directory => directory.TransitionAsync(rejected.Id, ChangeSetState.ChecksRunning), asPerson: false);
        await MoveAsync(rejected.Id, ChangeSetState.AwaitingReview, directory => directory.TransitionAsync(rejected.Id, ChangeSetState.AwaitingReview));
        await MoveAsync(rejected.Id, ChangeSetState.Rejected, directory => directory.RejectAsync(rejected.Id, "Not needed."));

        Assert.Equal(ChangeSetMoves.Operations.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    private static async Task ExpectMoveAsync(ChangeSetScenario scenario, Guid id, ChangeSetState state, HashSet<string> seen, IReadOnlyCollection<Guid> before)
    {
        var appended = (await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.SubjectType == "change_set" && row.SubjectId == id && row.Operation.StartsWith("change_set."))
            .ToListAsync()))
            .Where(row => !before.Contains(row.Id) && !row.Operation.StartsWith("change_set.operation_", StringComparison.Ordinal))
            .ToList();

        var move = Assert.Single(appended);
        Assert.Contains(move.Operation, ChangeSetMoves.Operations);
        Assert.Equal(state, ChangeSetMoves.StateAfter(move.Operation));
        seen.Add(move.Operation);
    }

    private static async Task<List<Guid>> MoveEventsAsync(ChangeSetScenario scenario, Guid id)
    {
        return await scenario.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.SubjectType == "change_set" && row.SubjectId == id)
            .Select(row => row.Id)
            .ToListAsync());
    }

    private static async Task<ChangeSet> AwaitingReviewAsync(ChangeSetScenario scenario, string slug)
    {
        var set = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync(slug)));
        await SubmitAsync(scenario, set.Id);
        await MoveAsPersonAsync(scenario, set.Id, ChangeSetState.AwaitingReview);
        return set;
    }

    private static async Task SubmitAsync(ChangeSetScenario scenario, Guid id)
    {
        Assert.True((await scenario.RunAsync(directory => directory.TransitionAsync(id, ChangeSetState.ChecksRunning))).IsSuccess);
    }

    private static async Task MoveAsPersonAsync(ChangeSetScenario scenario, Guid id, params ChangeSetState[] states)
    {
        foreach (var state in states)
        {
            var moved = await scenario.AsPersonAsync(directory => directory.TransitionAsync(id, state));
            Assert.True(moved.IsSuccess, string.Join("; ", moved.Errors.Select(error => error.Message)));
        }
    }

    private static async Task<ChangeSet> RevertAsync(ChangeSetScenario scenario, Guid id)
    {
        return (await scenario.AsPersonAsync(directory => directory.RevertAsync(id, "The prices were wrong."))).Value;
    }

    private static async Task<List<Guid>> IdsAsync(ChangeSetScenario scenario, DeskQueue queue)
    {
        var listed = await QueuesAsync(scenario, queues => queues.ListChangeSetsAsync(queue, 0, 50));
        return [.. listed.Value.Items.Select(set => set.Id)];
    }

    private static async Task<ChangeSetList> ListAsync(ChangeSetScenario scenario, ChangeSetListQuery query)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IChangeSetLists>().ListAsync(query)).Value;
    }

    // As a reviewer reads them; an agent's queues hold only its own sets.
    private static async Task<T> QueuesAsync<T>(ChangeSetScenario scenario, Func<IDeskQueues, Task<T>> read)
    {
        var initiator = scenario.Initiator;
        scenario.Initiator = Amir;
        try
        {
            await using var scope = scenario.Services.CreateAsyncScope();
            return await read(scope.ServiceProvider.GetRequiredService<IDeskQueues>());
        }
        finally
        {
            scenario.Initiator = initiator;
        }
    }

    private static (Guid? Reverts, Initiator Initiator, ChangeSetState State, int Items) Describe(ChangeSetSummary set)
    {
        return (set.RevertsChangeSetId, set.Initiator, set.State, set.ItemCount);
    }

    private static async Task<Guid> PricedAsync(EntryScenario scenario, string validUntil, string lastVerified)
    {
        const string sourced = """{"kind": "url", "reference": "https://example.com/catalog"}""";
        var created = await scenario.CreateAsync("product", $$"""
            {
              "name": {"value": "Drill", "source": {{sourced}}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
              "price": {"value": 99, "source": {{sourced}}, "owner": null, "validFrom": null, "validUntil": {{validUntil}}, "lastVerified": {{lastVerified}}}
            }
            """);
        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        return created.Value.Id;
    }
}
