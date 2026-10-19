using FluentCMS.Core;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Desk;

public sealed class DeskQueuesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly RecordingLists _lists = new();
    private readonly StaleFacts _facts = new();
    private readonly ServiceProvider _provider;

    public DeskQueuesTests()
    {
        _provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FixedClock(Now))
            .AddFluentCmsCore()
            .AddSingleton<IChangeSetLists>(_lists)
            .AddSingleton<IEntityFacts>(_facts)
            .BuildServiceProvider();
    }

    private IDeskQueues Queues => _provider.GetRequiredService<IDeskQueues>();

    [Theory]
    [InlineData(DeskQueue.AwaitingMe, ChangeSetState.AwaitingReview, false, false)]
    [InlineData(DeskQueue.AwaitingAnyone, ChangeSetState.AwaitingReview, false, false)]
    [InlineData(DeskQueue.ChecksFailed, ChangeSetState.ChecksFailed, false, false)]
    [InlineData(DeskQueue.RecentlyPublished, ChangeSetState.Published, true, true)]
    [InlineData(DeskQueue.Reverted, ChangeSetState.Reverted, true, true)]
    public async Task Each_queue_of_change_sets_selects_its_state_window_and_order(DeskQueue queue, ChangeSetState state, bool recent, bool newestFirst)
    {
        await Queues.ListChangeSetsAsync(queue, 50, 25);

        var query = Assert.Single(_lists.Listed);
        Assert.Equal([state], query.States);
        Assert.Equal(recent ? Now.AddDays(-7) : null, query.EnteredSince);
        Assert.Equal(newestFirst, query.NewestFirst);
        Assert.Equal((50, 25), (query.Offset, query.Limit));
    }

    [Fact]
    public async Task Counts_every_queue_and_the_stale_facts()
    {
        _lists.Counts[ChangeSetState.AwaitingReview] = 3;
        _lists.Counts[ChangeSetState.ChecksFailed] = 1;
        _lists.Counts[ChangeSetState.Published] = 7;
        _facts.Stale = [Fact(), Fact()];

        var counts = (await Queues.CountAsync()).Value;

        Assert.Equal(
            [(DeskQueue.AwaitingMe, 3), (DeskQueue.AwaitingAnyone, 3), (DeskQueue.ChecksFailed, 1), (DeskQueue.StaleFacts, 2), (DeskQueue.RecentlyPublished, 7), (DeskQueue.Reverted, 0)],
            counts.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value)));
        Assert.All(_lists.Counted.Where(query => query.States.Contains(ChangeSetState.Published)), query => Assert.Equal(Now.AddDays(-7), query.EnteredSince));
    }

    [Fact]
    public async Task Stale_facts_are_paged_in_their_order()
    {
        _facts.Stale = [Fact("a"), Fact("b"), Fact("c")];

        var page = (await Queues.ListStaleFactsAsync(1, 1)).Value;

        Assert.Equal(("b", 3), (Assert.Single(page.Items).Field, page.Total));
    }

    [Fact]
    public async Task A_failed_read_fails_the_counts()
    {
        _facts.Refuse = true;

        var counts = await Queues.CountAsync();
        var facts = await Queues.ListStaleFactsAsync(0, 10);

        Assert.Equal("type_not_entity", Assert.Single(counts.Errors).Code);
        Assert.Equal("type_not_entity", Assert.Single(facts.Errors).Code);
    }

    [Fact]
    public async Task Stale_facts_are_not_a_queue_of_change_sets()
    {
        var listed = await Queues.ListChangeSetsAsync(DeskQueue.StaleFacts, 0, 10);

        Assert.Equal(DeskErrorCodes.QueueNotChangeSets, Assert.Single(listed.Errors).Code);
        Assert.Empty(_lists.Listed);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static StaleFact Fact(string field = "price")
    {
        return new StaleFact(Guid.NewGuid(), "product", field, 1, true, false, StaleReason.Expired, Now.AddDays(-1));
    }

    private sealed class RecordingLists : IChangeSetLists
    {
        public List<ChangeSetListQuery> Listed { get; } = [];

        public List<ChangeSetListQuery> Counted { get; } = [];

        public Dictionary<ChangeSetState, int> Counts { get; } = [];

        public Task<Result<ChangeSetList>> ListAsync(ChangeSetListQuery query, CancellationToken cancellationToken = default)
        {
            Listed.Add(query);
            return Task.FromResult(Result<ChangeSetList>.Success(new ChangeSetList([], 0)));
        }

        public Task<Result<int>> CountAsync(ChangeSetListQuery query, CancellationToken cancellationToken = default)
        {
            Counted.Add(query);
            return Task.FromResult(Result<int>.Success(query.States.Sum(state => Counts.GetValueOrDefault(state))));
        }
    }

    private sealed class StaleFacts : IEntityFacts
    {
        public IReadOnlyList<StaleFact> Stale { get; set; } = [];

        public bool Refuse { get; set; }

        public Task<Result<IReadOnlyList<StaleFact>>> StaleAsync(string? contentType = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Refuse
                ? Result<IReadOnlyList<StaleFact>>.Failure(EntityErrorCodes.TypeNotEntity, "Not an entity type.")
                : Result<IReadOnlyList<StaleFact>>.Success(Stale));
        }

        public Task<Result<EntityFactSheet>> GetAsync(Guid entityId, int? version = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<IReadOnlyList<FactFlag>>> UnsourcedAsync(string? contentType = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Result<IReadOnlyList<FactFlag>>> UnsourcedInChangeSetAsync(Guid changeSetId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
