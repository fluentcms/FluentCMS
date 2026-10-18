using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// The stored change sets of the site; the initiator is whoever calls (CHG-01). Every write appends its audit event in the same
// unit of work, every transition included (AUD-N1).
public interface IChangeSetDirectory
{
    Task<Result<ChangeSet>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // Computed from the stored versions each operation names; a read, so it appends no event.
    Task<Result<ChangeSetDiff>> DiffAsync(Guid id, CancellationToken cancellationToken = default);

    // The open set the caller opened under this session id, the one AddToSessionAsync adds to; change_set_not_found when none is.
    Task<Result<ChangeSet>> GetSessionSetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> OpenAsync(NewChangeSet changeSet, CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> AddOperationAsync(Guid changeSetId, ChangeOperation operation, CancellationToken cancellationToken = default);

    // Adds the operation to the open set the caller opened under this session id, with any sources the set does not have yet,
    // or opens that set with the given rationale and sources. Once it leaves open, the next operation opens a new one (CHG-02).
    Task<Result<ChangeSet>> AddToSessionAsync(
        string sessionId,
        NewChangeSet changeSet,
        ChangeOperation operation,
        CancellationToken cancellationToken = default);

    // Moving to published publishes the set, as PublishAsync does. Moving to reverted is refused: a set is reverted by publishing
    // the set RevertAsync opens.
    Task<Result<ChangeSet>> TransitionAsync(Guid changeSetId, ChangeSetState target, CancellationToken cancellationToken = default);

    // Moves every item of an approved set from the version its operation was based on to the proposed one, together with the set
    // and the provenance of each item, or nothing at all (CHG-05, AUD-N1, REL-01). An item that is no longer published at its
    // base version fails the whole publish.
    Task<Result<ChangeSet>> PublishAsync(Guid changeSetId, CancellationToken cancellationToken = default);

    // Opens the set that undoes a published one and returns it (CHG-06). It goes through review like any set, and publishing it
    // puts every item back at the version it had before the original and moves the original to reverted, together.
    Task<Result<ChangeSet>> RevertAsync(Guid changeSetId, string rationale, CancellationToken cancellationToken = default);

    Task<Result<ChangeSet>> RejectAsync(Guid changeSetId, string reason, CancellationToken cancellationToken = default);
}
