using FluentCMS.Core.ChangeSets;

namespace FluentCMS.Core.Content;

// The CNT-01 states. In review is read from the change sets that hold the entry; the others are stored with it and moved only by
// publish, so no application service can set one directly.
public enum EntryState
{
    Draft,
    InReview,
    Published,
    Reverted,
    Archived,
}

public static class EntryStates
{
    // A set in one of these holds its items in review: submitted, and neither published nor rejected yet.
    public static IReadOnlyList<ChangeSetState> Review { get; } =
        [ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed, ChangeSetState.AwaitingReview, ChangeSetState.Approved];

    // What publishing an operation on the entry leaves it as; every item of a revert is reverted, whatever the operation.
    public static EntryState AfterPublish(OperationKind kind, bool revert)
    {
        return revert ? EntryState.Reverted : kind == OperationKind.Delete ? EntryState.Archived : EntryState.Published;
    }
}
