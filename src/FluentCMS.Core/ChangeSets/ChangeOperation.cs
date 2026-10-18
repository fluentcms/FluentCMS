using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// One create, update or delete in a change set. It names the item, the version the change was based on and the version it
// proposes, so the diff compares the two, publish can tell whether the item moved on in the meantime and revert knows what
// to restore (CHG-03, CHG-05, CHG-06).
public sealed record ChangeOperation
{
    private ChangeOperation(OperationKind kind, ItemKind itemKind, Guid itemId, int? baseVersion, int? proposedVersion)
    {
        Kind = kind;
        ItemKind = itemKind;
        ItemId = itemId;
        BaseVersion = baseVersion;
        ProposedVersion = proposedVersion;
    }

    public OperationKind Kind { get; }

    public ItemKind ItemKind { get; }

    public Guid ItemId { get; }

    // Null for a create, which has no earlier version.
    public int? BaseVersion { get; }

    // Null for a delete, which leaves no version behind.
    public int? ProposedVersion { get; }

    public static Result<ChangeOperation> Create(OperationKind kind, ItemKind itemKind, Guid itemId, int? baseVersion, int? proposedVersion)
    {
        var errors = new List<Error>();

        if (!Enum.IsDefined(kind))
        {
            errors.Add(new Error(ChangeSetErrorCodes.OperationKindInvalid, $"The operation '{kind}' is not one of create, update or delete."));
        }

        if (!Enum.IsDefined(itemKind))
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.ItemKindInvalid,
                $"The item kind '{itemKind}' is not one of entry, entity, media, schema or rule."));
        }

        if (itemId == Guid.Empty)
        {
            errors.Add(new Error(ChangeSetErrorCodes.ItemMissing, "An operation names its item, and the item id is empty."));
        }

        if (kind == OperationKind.Create && baseVersion is not null)
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.BaseVersionInvalid,
                $"A create has no base version because the item does not exist yet, but version {baseVersion} was given."));
        }

        if (kind is OperationKind.Update or OperationKind.Delete && baseVersion is not >= 1)
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.BaseVersionInvalid,
                $"An {kind.ToString().ToLowerInvariant()} names the version it was based on, which is 1 or later."));
        }

        if (kind == OperationKind.Create && proposedVersion is not >= 1)
        {
            errors.Add(new Error(ChangeSetErrorCodes.ProposedVersionInvalid, "A create names the version it proposes, which is 1 or later."));
        }

        if (kind == OperationKind.Update && baseVersion is >= 1 && !(proposedVersion > baseVersion))
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.ProposedVersionInvalid,
                $"An update names the version it proposes, which is later than its base version {baseVersion}."));
        }

        if (kind == OperationKind.Delete && proposedVersion is not null)
        {
            errors.Add(new Error(
                ChangeSetErrorCodes.ProposedVersionInvalid,
                $"A delete proposes no version because the item goes away, but version {proposedVersion} was given."));
        }

        return errors.Count == 0
            ? Result<ChangeOperation>.Success(new ChangeOperation(kind, itemKind, itemId, baseVersion, proposedVersion))
            : Result<ChangeOperation>.Failure(errors);
    }

    // The operation that puts the item back at the version this one was based on (CHG-06). An update back to an earlier version
    // exists only this way; Create still asks an update for a later version.
    public ChangeOperation Inverse()
    {
        return Kind switch
        {
            OperationKind.Create => new ChangeOperation(OperationKind.Delete, ItemKind, ItemId, ProposedVersion, null),
            OperationKind.Update => new ChangeOperation(OperationKind.Update, ItemKind, ItemId, ProposedVersion, BaseVersion),
            OperationKind.Delete => new ChangeOperation(OperationKind.Create, ItemKind, ItemId, null, BaseVersion),
            _ => throw new InvalidOperationException($"The operation '{Kind}' has no inverse."),
        };
    }

    // Rebuilds a stored operation without checking it again, so a set stays readable if the rules above change.
    public static ChangeOperation Restore(OperationKind kind, ItemKind itemKind, Guid itemId, int? baseVersion, int? proposedVersion)
    {
        return new ChangeOperation(kind, itemKind, itemId, baseVersion, proposedVersion);
    }
}
