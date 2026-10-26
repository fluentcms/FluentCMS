using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// The unit of every write and every review: operations grouped under one rationale, with the sources they came from and
// who started them (CHG-01, D15). It moves only along ChangeSetTransitions, and a refused call leaves it as it was.
public sealed class ChangeSet
{
    public const int MaxSessionIdLength = 128;

    private readonly List<string> _sources;
    private readonly List<ChangeOperation> _operations = [];

    private ChangeSet(Guid id, Guid siteId, string rationale, IEnumerable<string> sources, Initiator initiator, string? sessionId)
    {
        Id = id;
        SiteId = siteId;
        Rationale = rationale;
        _sources = [.. sources];
        Initiator = initiator;
        SessionId = sessionId;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public string Rationale { get; }

    public IReadOnlyList<string> Sources => _sources.AsReadOnly();

    public Initiator Initiator { get; }

    // The caller's session whose consecutive operations this set groups (CHG-02); null for a set opened explicitly.
    public string? SessionId { get; }

    public IReadOnlyList<ChangeOperation> Operations => _operations.AsReadOnly();

    public ChangeSetState State { get; private set; } = ChangeSetState.Open;

    // Kept so the desk can offer it as rule feedback once the rule book exists (CHG-08, RUL-08).
    public string? RejectionReason { get; private set; }

    // The published set this one undoes (CHG-06); null for every other set.
    public Guid? RevertsChangeSetId { get; private set; }

    // Operations come later, while the set is open, so an agent can open a set before its first write (CHG-02). An empty
    // list of sources is allowed here: requiring one is a check (CHK-01), not a rule of the model.
    public static Result<ChangeSet> Open(Guid siteId, string rationale, IEnumerable<string> sources, Initiator initiator, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(initiator);

        var errors = new List<Error>();
        var trimmedRationale = rationale?.Trim() ?? string.Empty;
        var trimmedSources = Trim(sources);

        if (siteId == Guid.Empty)
        {
            errors.Add(new Error(ChangeSetErrorCodes.SiteMissing, "A change set belongs to a site, and the site id is empty."));
        }

        // A set always has a person or an agent behind it (CHG-01); the product's own engines move sets, never open them (ADR-0012).
        if (initiator is SystemInitiator system)
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.InitiatorNotAllowed,
                $"A change set is opened by a person or an agent, and '{system.Component}' is part of the product."));
        }

        if (trimmedRationale.Length == 0)
        {
            errors.Add(new Error(ChangeSetErrorCodes.RationaleMissing, "A change set needs a rationale that says why the changes are made."));
        }

        errors.AddRange(BlankSources(trimmedSources));

        if (sessionId is not null && (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > MaxSessionIdLength))
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.SessionIdInvalid,
                $"A session id must not be blank and must be at most {MaxSessionIdLength} characters."));
        }

        return errors.Count == 0
            ? Result<ChangeSet>.Success(new ChangeSet(Guid.CreateVersion7(), siteId, trimmedRationale, trimmedSources.Distinct(), initiator, sessionId))
            : Result<ChangeSet>.Failure(errors);
    }

    // Rebuilds a stored set without checking it again, so it stays readable if the rules above change.
    public static ChangeSet Restore(
        Guid id,
        Guid siteId,
        string rationale,
        IEnumerable<string> sources,
        Initiator initiator,
        string? sessionId,
        IEnumerable<ChangeOperation> operations,
        ChangeSetState state,
        string? rejectionReason,
        Guid? revertsChangeSetId = null)
    {
        ArgumentNullException.ThrowIfNull(rationale);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(initiator);
        ArgumentNullException.ThrowIfNull(operations);

        var changeSet = new ChangeSet(id, siteId, rationale, sources, initiator, sessionId)
        {
            State = state,
            RejectionReason = rejectionReason,
            RevertsChangeSetId = revertsChangeSetId,
        };
        changeSet._operations.AddRange(operations);

        return changeSet;
    }

    // Opens the set that undoes this published one (CHG-06): the inverse of every operation, in the same order. It names this set
    // instead of citing sources, and holds nothing else, so publishing it restores exactly what this set changed.
    public Result<ChangeSet> Revert(string rationale, Initiator initiator)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        if (State != ChangeSetState.Published)
        {
            return Result<ChangeSet>.Failure(
                ChangeSetErrorCodes.TransitionNotAllowed,
                $"Only a published change set can be reverted, and this one is {Describe(State)}.");
        }

        var opened = Open(SiteId, rationale, [], initiator);
        if (!opened.IsSuccess)
        {
            return opened;
        }

        var revert = opened.Value;
        revert.RevertsChangeSetId = Id;
        revert._operations.AddRange(_operations.Select(operation => operation.Inverse()));

        return Result<ChangeSet>.Success(revert);
    }

    // Later operations in a session may cite sources the first one did not; a source already on the set is not added twice.
    public Result AddSources(IEnumerable<string> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (State != ChangeSetState.Open)
        {
            return Result.Failure(
                ChangeSetErrorCodes.NotOpen,
                $"Sources are added while a change set is open, and this one is {Describe(State)}.");
        }

        var trimmedSources = Trim(sources);
        var blank = BlankSources(trimmedSources);
        if (blank.Count > 0)
        {
            return Result.Failure(blank);
        }

        _sources.AddRange(trimmedSources.Distinct().Except(_sources));
        return Result.Success();
    }

    public Result AddOperation(ChangeOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var writable = CanTakeOperations();
        if (!writable.IsSuccess)
        {
            return writable;
        }

        // One operation per item keeps one diff per item and one version to restore on revert (CHG-03, CHG-06). A newer version of an
        // entry, entity or media asset the set already proposes, as when a reopened set is fixed, moves that operation on to it.
        var position = _operations.FindIndex(existing => existing.ItemKind == operation.ItemKind && existing.ItemId == operation.ItemId);
        if (position >= 0)
        {
            var existing = _operations[position];
            if (!Supersedes(existing, operation))
            {
                return Result.Failure(ChangeSetErrorCodes.ItemDuplicate, Duplicate(existing, operation));
            }

            _operations[position] = ChangeOperation.Restore(existing.Kind, existing.ItemKind, existing.ItemId, existing.BaseVersion, operation.ProposedVersion);
            return Result.Success();
        }

        _operations.Add(operation);
        return Result.Success();
    }

    // A write of an item adds its operation, or extends the one the set already holds for the item, so the set still has one
    // operation per item (SCH-05): the write is based on the version the set proposes, and the operation now proposes the write's
    // version. A create stays a create and an update keeps its base. An update of an item no set has published, or that a published
    // delete took offline (a rejected create, a reverted one), is recorded as a create, since publishing it makes the item live again
    // from nothing.
    public Result Record(ChangeOperation operation, bool itemPublished = true)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var index = _operations.FindIndex(existing => existing.ItemKind == operation.ItemKind && existing.ItemId == operation.ItemId);
        if (index < 0)
        {
            return AddOperation(itemPublished || operation.Kind != OperationKind.Update
                ? operation
                : ChangeOperation.Create(OperationKind.Create, operation.ItemKind, operation.ItemId, null, operation.ProposedVersion).Value);
        }

        var writable = CanTakeOperations();
        if (!writable.IsSuccess)
        {
            return writable;
        }

        var current = _operations[index];
        if (operation.Kind != OperationKind.Update || current.ProposedVersion is not { } proposed || operation.BaseVersion != proposed)
        {
            var proposes = current.ProposedVersion is { } version ? $"version {version}" : "its deletion";
            return Result.Failure(
                ChangeSetErrorCodes.ItemDuplicate,
                $"The {Describe(operation.ItemKind)} {operation.ItemId} already has an operation in this change set, which proposes {proposes}; "
                + "a later write of it in this set is an update based on that version.");
        }

        _operations[index] = ChangeOperation.Create(current.Kind, current.ItemKind, current.ItemId, current.BaseVersion, operation.ProposedVersion).Value;
        return Result.Success();
    }

    public Result TransitionTo(ChangeSetState target)
    {
        var allowed = CanMoveTo(target);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        if (target == ChangeSetState.Rejected)
        {
            return Result.Failure(
                ChangeSetErrorCodes.RejectionReasonMissing,
                "A rejection needs a reason, which the change set stores; reject it with one.");
        }

        State = target;
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        var allowed = CanMoveTo(ChangeSetState.Rejected);
        if (!allowed.IsSuccess)
        {
            return allowed;
        }

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
        {
            return Result.Failure(
                ChangeSetErrorCodes.RejectionReasonMissing,
                "A rejection needs a reason that says what is wrong, and the reason is blank.");
        }

        State = ChangeSetState.Rejected;
        RejectionReason = trimmedReason;
        return Result.Success();
    }

    private Result CanTakeOperations()
    {
        if (State != ChangeSetState.Open)
        {
            return Result.Failure(
                ChangeSetErrorCodes.NotOpen,
                $"Operations are added while a change set is open, and this one is {Describe(State)}.");
        }

        if (RevertsChangeSetId is { } reverted)
        {
            return Result.Failure(
                ChangeSetErrorCodes.RevertFixed,
                $"This change set reverts change set {reverted} and holds exactly what undoes it, so no operation can be added.");
        }

        return Result.Success();
    }

    private Result CanMoveTo(ChangeSetState target)
    {
        if (!ChangeSetTransitions.IsAllowed(State, target))
        {
            var next = ChangeSetTransitions.From(State);
            var options = next.Count == 0
                ? $"{Describe(State)} is final"
                : "it can move to " + string.Join(" or ", next.Select(Describe));

            return Result.Failure(
                ChangeSetErrorCodes.TransitionNotAllowed,
                $"A change set cannot move from {Describe(State)} to {Describe(target)}; {options}.");
        }

        // A change set contains one or more operations (CHG-01); while open it may still be empty.
        if (State == ChangeSetState.Open && _operations.Count == 0)
        {
            return Result.Failure(ChangeSetErrorCodes.OperationsMissing, "A change set needs at least one operation before it leaves open.");
        }

        return Result.Success();
    }

    private static List<string> Trim(IEnumerable<string> sources)
    {
        return sources.Select(source => source?.Trim() ?? string.Empty).ToList();
    }

    private static List<Error> BlankSources(List<string> sources)
    {
        return sources
            .Select((source, index) => (source, index))
            .Where(candidate => candidate.source.Length == 0)
            .Select(candidate => new Error(
                ChangeSetErrorCodes.SourceBlank,
                $"Source {candidate.index + 1} is blank. A source names where a change came from, such as a URL, a file, an export or a person."))
            .ToList();
    }

    private static string Describe(ChangeSetState state)
    {
        return state switch
        {
            ChangeSetState.ChecksRunning => "checks running",
            ChangeSetState.ChecksFailed => "checks failed",
            ChangeSetState.AwaitingReview => "awaiting review",
            _ => state.ToString().ToLowerInvariant(),
        };
    }

    // An item published by another set since this one proposed it changes kind or base, and this set can no longer change it.
    private static string Duplicate(ChangeOperation existing, ChangeOperation operation)
    {
        var item = $"The {Describe(operation.ItemKind)} {operation.ItemId}";
        if (operation.ItemKind is not (ItemKind.Entry or ItemKind.Entity or ItemKind.Media) || existing.ProposedVersion is not { } proposed)
        {
            return $"{item} already has an operation in this change set.";
        }

        return operation.Kind != existing.Kind || operation.BaseVersion != existing.BaseVersion
            ? $"{item} was published by another change set after this one proposed it, so this set can no longer change it; propose the change in a new change set."
            : $"{item} already has an operation in this change set; update it from version {proposed}, which the set proposes, to change what it proposes.";
    }

    // The same change from the same published version, proposing a later draft.
    private static bool Supersedes(ChangeOperation existing, ChangeOperation operation)
    {
        return operation.ItemKind is ItemKind.Entry or ItemKind.Entity or ItemKind.Media
            && operation.Kind is OperationKind.Create or OperationKind.Update
            && operation.Kind == existing.Kind
            && operation.BaseVersion == existing.BaseVersion
            && operation.ProposedVersion > existing.ProposedVersion;
    }

    private static string Describe(ItemKind kind)
    {
        return kind.ToString().ToLowerInvariant();
    }
}
