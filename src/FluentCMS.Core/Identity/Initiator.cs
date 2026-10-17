namespace FluentCMS.Core.Identity;

// Who caused a change: a person or an agent, never confusable in code or in the audit log (ADR-0009, AUD-01).
public abstract record Initiator(string DisplayName);

public sealed record UserInitiator(Guid UserId, string DisplayName) : Initiator(DisplayName);

public sealed record AgentInitiator(Guid CredentialId, string DisplayName) : Initiator(DisplayName);
