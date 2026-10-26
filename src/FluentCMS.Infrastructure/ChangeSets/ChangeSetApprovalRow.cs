namespace FluentCMS.Infrastructure.ChangeSets;

// One person's approval of a set in review; inserted once and never changed.
public sealed class ChangeSetApprovalRow
{
    public Guid ChangeSetId { get; set; }

    public Guid UserId { get; set; }

    public string ApproverName { get; set; } = string.Empty;

    public DateTimeOffset ApprovedAt { get; set; }
}
