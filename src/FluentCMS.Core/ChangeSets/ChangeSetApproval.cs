using FluentCMS.Core.Identity;

namespace FluentCMS.Core.ChangeSets;

// One person's approval of a set in review (RTE-05).
public sealed record ChangeSetApproval(UserInitiator Approver, DateTimeOffset ApprovedAt);
