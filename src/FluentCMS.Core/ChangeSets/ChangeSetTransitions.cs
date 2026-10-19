namespace FluentCMS.Core.ChangeSets;

// The CHG-04 state machine. A change set moves only along these pairs and every other move is refused, so a new transition
// is a change to this table and nowhere else.
public static class ChangeSetTransitions
{
    public static IReadOnlyList<(ChangeSetState From, ChangeSetState To)> All { get; } =
    [
        (ChangeSetState.Open, ChangeSetState.ChecksRunning),
        (ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed),
        (ChangeSetState.ChecksRunning, ChangeSetState.AwaitingReview),

        // Routing auto-publishes: nobody approves, but publish keeps a single way in (RTE-01, MCP-06).
        (ChangeSetState.ChecksRunning, ChangeSetState.Approved),
        (ChangeSetState.ChecksFailed, ChangeSetState.Open),
        (ChangeSetState.ChecksFailed, ChangeSetState.Rejected),
        (ChangeSetState.AwaitingReview, ChangeSetState.Approved),
        (ChangeSetState.AwaitingReview, ChangeSetState.Rejected),
        (ChangeSetState.Approved, ChangeSetState.Published),

        // An approved set that can no longer publish, such as one whose base another set published first, is cleared by a person.
        (ChangeSetState.Approved, ChangeSetState.Rejected),
        (ChangeSetState.Published, ChangeSetState.Reverted),
    ];

    public static bool IsAllowed(ChangeSetState from, ChangeSetState to)
    {
        return All.Contains((from, to));
    }

    public static IReadOnlyList<ChangeSetState> From(ChangeSetState state)
    {
        return All.Where(transition => transition.From == state).Select(transition => transition.To).ToList();
    }
}
