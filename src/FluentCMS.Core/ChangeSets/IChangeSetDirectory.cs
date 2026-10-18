using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// The stored change sets of the site; the initiator is whoever calls (CHG-01). Every write appends its audit event in the same
// unit of work, every transition included (AUD-N1).
public interface IChangeSetDirectory
{
    Task<Result<ChangeSet>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // Computed from the stored versions each operation names; a read, so it appends no event.
    Task<Result<ChangeSetDiff>> DiffAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> OpenAsync(NewChangeSet changeSet, CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> AddOperationAsync(Guid changeSetId, ChangeOperation operation, CancellationToken cancellationToken = default);

    // Adds the operation to the open set the caller opened under this session id, with any sources the set does not have yet,
    // or opens that set with the given rationale and sources. Once it leaves open, the next operation opens a new one (CHG-02).
    Task<Result<ChangeSet>> AddToSessionAsync(
        string sessionId,
        NewChangeSet changeSet,
        ChangeOperation operation,
        CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> TransitionAsync(Guid changeSetId, ChangeSetState target, CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> RejectAsync(Guid changeSetId, string reason, CancellationToken cancellationToken = default);
}
