using FluentCMS.Core.ChangeSets;

namespace FluentCMS.Infrastructure.ChangeSets;

// A change set without its operations, which are ChangeSetOperationRows.
public sealed class ChangeSetRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public List<string> Sources { get; set; } = [];

    public string InitiatorKind { get; set; } = string.Empty;

    public Guid InitiatorId { get; set; }

    public string InitiatorName { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    public ChangeSetState State { get; set; }

    public string? RejectionReason { get; set; }

    // Every write moves it on and only applies to the revision it read, so of two concurrent writers the second changes nothing.
    public int Revision { get; set; }
}
