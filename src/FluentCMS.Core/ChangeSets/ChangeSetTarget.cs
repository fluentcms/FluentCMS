namespace FluentCMS.Core.ChangeSets;

// The change set a write of an item joins (SCH-05): one the caller opened, or the open set of the caller's session, which the
// first write opens with the given rationale and sources (CHG-02).
public sealed record ChangeSetTarget
{
    private ChangeSetTarget(Guid? changeSetId, string? sessionId, NewChangeSet? newChangeSet)
    {
        ChangeSetId = changeSetId;
        SessionId = sessionId;
        NewChangeSet = newChangeSet;
    }

    public Guid? ChangeSetId { get; }

    public string? SessionId { get; }

    // The rationale and sources of the set a session's first write opens; later writes add any new sources to it.
    public NewChangeSet? NewChangeSet { get; }

    public static ChangeSetTarget Existing(Guid changeSetId)
    {
        return new ChangeSetTarget(changeSetId, null, null);
    }

    public static ChangeSetTarget Session(string sessionId, NewChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(changeSet);

        return new ChangeSetTarget(null, sessionId, changeSet);
    }
}
