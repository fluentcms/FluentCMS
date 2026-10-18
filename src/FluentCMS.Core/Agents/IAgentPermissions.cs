using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Agents;

// What the current caller may do (MCP-06, SEC-02, SEC-04), decided in Core so tools and forms cannot diverge (ADR-0007).
// People pass every check. An agent is held to its stored credential, read again for the call, so a credential revoked or
// expired since its key was accepted, or one whose scope lacks the site, is refused. A refused cap or move is recorded as a
// security event (SEC-08); the refused write itself is left to the caller, which writes nothing.
public interface IAgentPermissions
{
    // An agent submits its set and reopens it after failed checks; every other move is the checks', routing's or a person's.
    Task<Result> CheckTransitionAsync(Guid changeSetId, ChangeSetState target, CancellationToken cancellationToken = default);

    Task<Result> CheckNewChangeSetAsync(CancellationToken cancellationToken = default);

    // Called with the set as read for the write and the new operation added in memory, before anything is written.
    Task<Result> CheckChangeSetSizeAsync(ChangeSet changeSet, CancellationToken cancellationToken = default);
}
