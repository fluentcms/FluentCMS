using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// The change sets in any of the given states, optionally only those that entered their state at or after a time, one page at a time.
public sealed record ChangeSetListQuery
{
    public const int MaxLimit = 200;

    public required IReadOnlyCollection<ChangeSetState> States { get; init; }

    public DateTimeOffset? EnteredSince { get; init; }

    // Only the sets this person or agent opened; null for every set.
    public Initiator? OpenedBy { get; init; }

    // Only the sets that revert this set; null for every set.
    public Guid? Reverts { get; init; }

    // Newest first puts the set that entered its state last first; otherwise the set that has waited longest comes first.
    public bool NewestFirst { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 50;

    public Result Validate()
    {
        var errors = new List<Error>();

        if (States is null || States.Count == 0 || States.Any(state => !Enum.IsDefined(state)))
        {
            errors.Add(new Error(ChangeSetErrorCodes.ListStatesMissing, "A list names one or more change set states to list."));
        }

        errors.AddRange(WindowErrors(Offset, Limit));

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    // The same window applies to the other lists the desk pages through, such as its stale facts.
    public static IEnumerable<Error> WindowErrors(int offset, int limit)
    {
        if (offset < 0)
        {
            yield return new Error(ChangeSetErrorCodes.OffsetInvalid, $"The offset is {offset}; it is 0 or more.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            yield return new Error(ChangeSetErrorCodes.LimitInvalid, $"The limit is {limit}; it is from 1 to {MaxLimit}.");
        }
    }
}
