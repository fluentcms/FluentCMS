using FluentCMS.Core.Results;

namespace FluentCMS.Core.ChangeSets;

// One create, update or delete in a change set. It names the item and the version the change was based on, so publish can
// tell whether the item moved on in the meantime and revert knows what to restore (CHG-05, CHG-06).
public sealed record ChangeOperation
{
    private ChangeOperation(OperationKind kind, ItemKind itemKind, Guid itemId, int? baseVersion)
    {
        Kind = kind;
        ItemKind = itemKind;
        ItemId = itemId;
        BaseVersion = baseVersion;
    }

    public OperationKind Kind { get; }

    public ItemKind ItemKind { get; }

    public Guid ItemId { get; }

    // Null for a create, which has no earlier version.
    public int? BaseVersion { get; }

    public static Result<ChangeOperation> Create(OperationKind kind, ItemKind itemKind, Guid itemId, int? baseVersion)
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

        return errors.Count == 0
            ? Result<ChangeOperation>.Success(new ChangeOperation(kind, itemKind, itemId, baseVersion))
            : Result<ChangeOperation>.Failure(errors);
    }
}
