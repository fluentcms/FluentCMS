namespace FluentCMS.Core.ChangeSets;

// The audit operations the change set directory appends, with the set as the subject, when it opens a set and on each move between
// the CHG-04 states. Other events about a set, such as a refused move, are not moves. Read sides date a set and list its review
// decisions from these.
public static class ChangeSetMoves
{
    public const string Opened = "change_set.opened";

    public static IReadOnlyList<string> Operations { get; } =
    [
        Opened,
        "change_set.reopened",
        "change_set.submitted",
        "change_set.checks_failed",
        "change_set.awaiting_review",
        "change_set.approved",
        "change_set.published",
        "change_set.rejected",
        "change_set.reverted",
    ];
}
