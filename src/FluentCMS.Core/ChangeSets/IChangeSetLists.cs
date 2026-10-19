using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// The read side for lists of change sets, for the desk's queues and the agents' desk tools (DSK-02, MCP-01). IChangeSetDirectory
// reads and writes one set at a time; this only reads. The times come from each set's own audit events. A list appends no event.
public interface IChangeSetLists
{
    Task<Result<ChangeSetList>> ListAsync(ChangeSetListQuery query, CancellationToken cancellationToken = default);

    // How many sets the query selects, whatever its offset and limit.
    Task<Result<int>> CountAsync(ChangeSetListQuery query, CancellationToken cancellationToken = default);
}

// One page of a list and how many sets the whole list holds.
public sealed record ChangeSetList(IReadOnlyList<ChangeSetSummary> Items, int Total);

// A change set as a list shows it: how many items it has rather than its operations. StateSince is when it entered its state.
public sealed record ChangeSetSummary(
    Guid Id,
    string Rationale,
    Initiator Initiator,
    ChangeSetState State,
    int ItemCount,
    Guid? RevertsChangeSetId,
    DateTimeOffset OpenedAt,
    DateTimeOffset StateSince);
