using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Desk;

// An agent reads its own change sets only (IAgentPermissions.CheckChangeSetReadAsync), so an agent's queues hold the sets it opened.
internal sealed class DeskQueues(IChangeSetLists lists, IEntityFacts facts, TimeProvider clock, ICurrentInitiator currentInitiator) : IDeskQueues
{
    // How far back the recently published and reverted queues reach.
    public static readonly TimeSpan RecentWithin = TimeSpan.FromDays(7);

    private static readonly DeskQueue[] ChangeSetQueues =
    [
        DeskQueue.AwaitingMe,
        DeskQueue.AwaitingAnyone,
        DeskQueue.ChecksFailed,
        DeskQueue.RecentlyPublished,
        DeskQueue.Reverted,
    ];

    public async Task<Result<IReadOnlyDictionary<DeskQueue, int>>> CountAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var counts = new Dictionary<DeskQueue, int>();

        foreach (var queue in ChangeSetQueues.Where(queue => queue != DeskQueue.AwaitingMe))
        {
            var counted = await lists.CountAsync(Scoped(Query(queue, now)), cancellationToken);
            if (!counted.IsSuccess)
            {
                return Result<IReadOnlyDictionary<DeskQueue, int>>.Failure(counted.Errors);
            }

            counts[queue] = counted.Value;
        }

        // The same sets as awaiting anyone until approvals exist; see Query.
        counts[DeskQueue.AwaitingMe] = counts[DeskQueue.AwaitingAnyone];

        var stale = await facts.StaleAsync(cancellationToken: cancellationToken);
        if (!stale.IsSuccess)
        {
            return Result<IReadOnlyDictionary<DeskQueue, int>>.Failure(stale.Errors);
        }

        counts[DeskQueue.StaleFacts] = stale.Value.Count;
        return Result<IReadOnlyDictionary<DeskQueue, int>>.Success(counts);
    }

    public async Task<Result<int>> CountAsync(DeskQueue queue, CancellationToken cancellationToken = default)
    {
        if (queue == DeskQueue.StaleFacts)
        {
            var stale = await facts.StaleAsync(cancellationToken: cancellationToken);
            return stale.IsSuccess ? Result<int>.Success(stale.Value.Count) : Result<int>.Failure(stale.Errors);
        }

        return await lists.CountAsync(Scoped(Query(queue, clock.GetUtcNow())), cancellationToken);
    }

    public Task<Result<ChangeSetList>> ListChangeSetsAsync(DeskQueue queue, int offset, int limit, CancellationToken cancellationToken = default)
    {
        if (!ChangeSetQueues.Contains(queue))
        {
            return Task.FromResult(Result<ChangeSetList>.Failure(
                DeskErrorCodes.QueueNotChangeSets,
                $"The {queue} queue does not list change sets; it is one of {string.Join(", ", ChangeSetQueues)}."));
        }

        return lists.ListAsync(Scoped(Query(queue, clock.GetUtcNow())) with { Offset = offset, Limit = limit }, cancellationToken);
    }

    public async Task<Result<StaleFactList>> ListStaleFactsAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        var window = ChangeSetListQuery.WindowErrors(offset, limit).ToList();
        if (window.Count > 0)
        {
            return Result<StaleFactList>.Failure(window);
        }

        var stale = await facts.StaleAsync(cancellationToken: cancellationToken);
        return stale.IsSuccess
            ? Result<StaleFactList>.Success(new StaleFactList([.. stale.Value.Skip(offset).Take(limit)], stale.Value.Count))
            : Result<StaleFactList>.Failure(stale.Errors);
    }

    private ChangeSetListQuery Scoped(ChangeSetListQuery query)
    {
        return currentInitiator.Current is AgentInitiator agent ? query with { OpenedBy = agent } : query;
    }

    // Awaiting me lists every set awaiting review until routing records approvals (F-RTE); it will then leave out the sets the
    // reviewer has approved. Work queues put the set that has waited longest first; the recent ones the newest.
    private static ChangeSetListQuery Query(DeskQueue queue, DateTimeOffset now)
    {
        return queue switch
        {
            DeskQueue.AwaitingMe or DeskQueue.AwaitingAnyone => new ChangeSetListQuery { States = [ChangeSetState.AwaitingReview] },
            DeskQueue.ChecksFailed => new ChangeSetListQuery { States = [ChangeSetState.ChecksFailed] },
            DeskQueue.RecentlyPublished => new ChangeSetListQuery
            {
                States = [ChangeSetState.Published],
                EnteredSince = now - RecentWithin,
                NewestFirst = true,
            },
            DeskQueue.Reverted => new ChangeSetListQuery
            {
                States = [ChangeSetState.Reverted],
                EnteredSince = now - RecentWithin,
                NewestFirst = true,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, "Not a queue of change sets."),
        };
    }
}
