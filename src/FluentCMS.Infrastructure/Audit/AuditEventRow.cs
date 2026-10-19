using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;

namespace FluentCMS.Infrastructure.Audit;

// The flat shape of an AuditEvent in the AuditEvents table; rows are inserted and never changed.
public sealed class AuditEventRow
{
    public Guid Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string InitiatorKind { get; set; } = string.Empty;

    public Guid InitiatorId { get; set; }

    public string InitiatorName { get; set; } = string.Empty;

    public Guid? AgentCredentialId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string? SubjectType { get; set; }

    public Guid? SubjectId { get; set; }

    public string? Model { get; set; }

    public string? PromptHash { get; set; }

    public List<string> Sources { get; set; } = [];

    public Guid? ChangeSetId { get; set; }

    public Guid? PreviousVersionId { get; set; }

    internal static AuditEventRow From(AuditEvent auditEvent)
    {
        var (kind, initiatorId) = InitiatorKinds.Describe(auditEvent.Initiator);

        return new AuditEventRow
        {
            Id = auditEvent.Id,
            Timestamp = auditEvent.Timestamp,
            InitiatorKind = kind,
            InitiatorId = initiatorId,
            InitiatorName = auditEvent.Initiator.DisplayName,
            AgentCredentialId = auditEvent.AgentCredentialId,
            Operation = auditEvent.Operation,
            SubjectType = auditEvent.SubjectType,
            SubjectId = auditEvent.SubjectId,
            Model = auditEvent.Model,
            PromptHash = auditEvent.PromptHash,
            Sources = [.. auditEvent.Sources],
            ChangeSetId = auditEvent.ChangeSetId,
            PreviousVersionId = auditEvent.PreviousVersionId,
        };
    }

    internal AuditEvent ToEvent()
    {
        Initiator initiator = InitiatorKind switch
        {
            InitiatorKinds.User => new UserInitiator(InitiatorId, InitiatorName),
            InitiatorKinds.Agent => new AgentInitiator(InitiatorId, InitiatorName),
            InitiatorKinds.System => new SystemInitiator(InitiatorName),
            _ => throw new InvalidOperationException($"Audit event {Id} names an unknown initiator kind '{InitiatorKind}'."),
        };

        return new AuditEvent
        {
            Id = Id,
            Timestamp = Timestamp,
            Initiator = initiator,
            Operation = Operation,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Model = Model,
            PromptHash = PromptHash,
            Sources = Sources,
            ChangeSetId = ChangeSetId,
            PreviousVersionId = PreviousVersionId,
        };
    }
}
