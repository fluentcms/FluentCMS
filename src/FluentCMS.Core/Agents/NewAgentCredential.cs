namespace FluentCMS.Core.Agents;

public sealed record NewAgentCredential(string DisplayName, DateTimeOffset ExpiresAt, AgentScope Scope, AgentCaps Caps);
