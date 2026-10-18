using FluentCMS.Core.ChangeSets;

namespace FluentCMS.Infrastructure.ChangeSets;

// One operation of a change set, at its position in the set; inserted once and never changed.
public sealed class ChangeSetOperationRow
{
    public Guid Id { get; set; }

    public Guid ChangeSetId { get; set; }

    public int Position { get; set; }

    public OperationKind Kind { get; set; }

    public ItemKind ItemKind { get; set; }

    public Guid ItemId { get; set; }

    public int? BaseVersion { get; set; }

    public int? ProposedVersion { get; set; }
}
