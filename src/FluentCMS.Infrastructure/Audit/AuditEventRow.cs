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
        var (kind, initiatorId) = auditEvent.Initiator switch
        {
            UserInitiator user => (InitiatorKinds.User, user.UserId),
            AgentInitiator agent => (InitiatorKinds.Agent, agent.CredentialId),
            _ => throw new NotSupportedException($"The audit log cannot store an initiator of type {auditEvent.Initiator.GetType().Name}."),
        };

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
}

public static class InitiatorKinds
{
    public const string User = "user";
    public const string Agent = "agent";
}
