using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Desk;

// The six queues of the review desk (DSK-02). Stale facts lists facts; every other queue lists change sets.
public enum DeskQueue
{
    AwaitingMe,
    AwaitingAnyone,
    ChecksFailed,
    StaleFacts,
    RecentlyPublished,
    Reverted,
}

// Each queue is defined here once, for the desk and for the agents' desk tools (MCP-01). Reads only, so no event is appended.
public interface IDeskQueues
{
    // How many sets or facts each of the six queues holds now.
    Task<Result<IReadOnlyDictionary<DeskQueue, int>>> CountAsync(CancellationToken cancellationToken = default);

    // How many one queue holds, for a count shown on every page, such as awaiting me in the header (DSK-05).
    Task<Result<int>> CountAsync(DeskQueue queue, CancellationToken cancellationToken = default);

    // A page of a queue of change sets; stale facts is listed by ListStaleFactsAsync.
    Task<Result<ChangeSetList>> ListChangeSetsAsync(DeskQueue queue, int offset, int limit, CancellationToken cancellationToken = default);

    Task<Result<StaleFactList>> ListStaleFactsAsync(int offset, int limit, CancellationToken cancellationToken = default);
}

public sealed record StaleFactList(IReadOnlyList<StaleFact> Items, int Total);
