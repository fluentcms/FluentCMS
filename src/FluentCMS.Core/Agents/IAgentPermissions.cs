using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Agents;

// What the current caller may do (MCP-06, SEC-02, SEC-04), decided in Core so tools and forms cannot diverge (ADR-0007).
// People pass every check. An agent is held to its stored credential, read again for the call, so a credential revoked or
// expired since its key was accepted, or one whose scope lacks the site, is refused. A system initiator makes only the moves its
// component owns and reads content, and is refused everything else (ADR-0012). A refused cap or move is recorded as a
// security event (SEC-08); the refused write itself is left to the caller, which writes nothing.
public interface IAgentPermissions
{
    // An agent submits its own set and reopens it after failed checks; every other move is the checks', routing's or a person's.
    Task<Result> CheckTransitionAsync(ChangeSet changeSet, ChangeSetState target, CancellationToken cancellationToken = default);

    // Called inside the unit of work that opens the set: it holds the agent's credential until that unit of work ends, so concurrent
    // opens by one agent are counted one at a time. Outside one it is only a pre-check, and the open counts again.
    Task<Result> CheckNewChangeSetAsync(CancellationToken cancellationToken = default);

    // An agent reads the change sets it opened, as it writes only those; people and the system read every set.
    Task<Result> CheckChangeSetReadAsync(ChangeSet changeSet, CancellationToken cancellationToken = default);

    // Called with the agent's own set as read for the write and the new operation added in memory, before anything is written,
    // or ahead of a write with the number of operations it will add.
    Task<Result> CheckChangeSetSizeAsync(ChangeSet changeSet, int adding = 0, CancellationToken cancellationToken = default);

    // The operation on items of the content type (MCP-05). A write names the fields it changes, which a field list in the scope can
    // narrow; a read names none, so an agent reads whole entries of a type in its scope.
    Task<Result> CheckContentAsync(
        string contentType,
        AgentOperation operation,
        IReadOnlyCollection<string>? fields = null,
        CancellationToken cancellationToken = default);

    // A content type's own definition, which covers every field, so a scope that narrows the type to some of its fields is refused.
    Task<Result> CheckDefinitionAsync(string contentType, AgentOperation operation, CancellationToken cancellationToken = default);

    // For lists: every type for a person, the types in scope for an agent that may read, and none for a refused credential.
    Task<Func<string, bool>> ReadableTypesAsync(CancellationToken cancellationToken = default);

    // Records the call before its tool runs: as running when it is within the credential's calls per period (SEC-04), or as refused,
    // with its security event, when it is not. The count and the record share one transaction that one credential's calls take turns on.
    Task<Result> ClaimToolCallAsync(ToolCall call, CancellationToken cancellationToken = default);
}
