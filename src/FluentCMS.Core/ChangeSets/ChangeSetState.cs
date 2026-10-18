namespace FluentCMS.Core.ChangeSets;

// The CHG-04 states; ChangeSetTransitions says which moves between them exist.
public enum ChangeSetState
{
    Open,
    ChecksRunning,
    ChecksFailed,
    AwaitingReview,
    Approved,
    Published,
    Rejected,
    Reverted,
}
