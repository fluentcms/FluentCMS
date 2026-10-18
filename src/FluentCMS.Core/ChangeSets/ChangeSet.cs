using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// The unit of every write and every review: operations grouped under one rationale, with the sources they came from and
// who started them (CHG-01, D15). It moves only along ChangeSetTransitions, and a refused call leaves it as it was.
public sealed class ChangeSet
{
    private readonly List<ChangeOperation> _operations = [];

    private ChangeSet(Guid id, Guid siteId, string rationale, IReadOnlyList<string> sources, Initiator initiator)
    {
        Id = id;
        SiteId = siteId;
        Rationale = rationale;
        Sources = sources;
        Initiator = initiator;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public string Rationale { get; }

    public IReadOnlyList<string> Sources { get; }

    public Initiator Initiator { get; }

    public IReadOnlyList<ChangeOperation> Operations => _operations.AsReadOnly();

    public ChangeSetState State { get; private set; } = ChangeSetState.Open;

    // Kept so the desk can offer it as rule feedback once the rule book exists (CHG-08, RUL-08).
    public string? RejectionReason { get; private set; }

    // Operations come later, while the set is open, so an agent can open a set before its first write (CHG-02). An empty
    // list of sources is allowed here: requiring one is a check (CHK-01), not a rule of the model.
    public static Result<ChangeSet> Open(Guid siteId, string rationale, IEnumerable<string> sources, Initiator initiator)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(initiator);

        var errors = new List<Error>();
        var trimmedRationale = rationale?.Trim() ?? string.Empty;
        var trimmedSources = sources.Select(source => source?.Trim() ?? string.Empty).ToList();

        if (siteId == Guid.Empty)
        {
            errors.Add(new Error(ChangeSetErrorCodes.SiteMissing, "A change set belongs to a site, and the site id is empty."));
        }

        if (trimmedRationale.Length == 0)
        {
            errors.Add(new Error(ChangeSetErrorCodes.RationaleMissing, "A change set needs a rationale that says why the changes are made."));
        }

        for (var index = 0; index < trimmedSources.Count; index++)
        {
            if (trimmedSources[index].Length == 0)
            {
                errors.Add(new Error(
                    ChangeSetErrorCodes.SourceBlank,
                    $"Source {index + 1} is blank. A source names where a change came from, such as a URL, a file, an export or a person."));
            }
        }

        return errors.Count == 0
            ? Result<ChangeSet>.Success(new ChangeSet(Guid.CreateVersion7(), siteId, trimmedRationale, trimmedSources, initiator))
            : Result<ChangeSet>.Failure(errors);
    }

    public Result AddOperation(ChangeOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (State != ChangeSetState.Open)
        {
            return Result.Failure(
                ChangeSetErrorCodes.NotOpen,
                $"Operations are added while a change set is open, and this one is {Describe(State)}.");
        }

        // One operation per item keeps one diff per item and one version to restore on revert (CHG-03, CHG-06).
        if (_operations.Any(existing => existing.ItemKind == operation.ItemKind && existing.ItemId == operation.ItemId))
        {
            return Result.Failure(
                ChangeSetErrorCodes.ItemDuplicate,
                $"The {Describe(operation.ItemKind)} {operation.ItemId} already has an operation in this change set.");
        }

        _operations.Add(operation);
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

    private static string Describe(ItemKind kind)
    {
        return kind.ToString().ToLowerInvariant();
    }
}
