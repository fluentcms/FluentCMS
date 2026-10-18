using FluentCMS.Core.Agents;

namespace FluentCMS.Infrastructure.Agents;

// An AgentCredential with its key's hash; the key itself is never stored (SEC-01).
public sealed class AgentCredentialRow
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public byte[] KeyHash { get; set; } = [];

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public List<Guid> Sites { get; set; } = [];

    // Null for every type, and Fields for every field, as in AgentScope.
    public List<string>? Types { get; set; }

    public List<string>? Fields { get; set; }

    public List<AgentOperation> Operations { get; set; } = [];

    public int CallsPerPeriod { get; set; }

    public TimeSpan CallPeriod { get; set; }

    public decimal LlmSpendPerPeriod { get; set; }

    public TimeSpan LlmSpendPeriod { get; set; }

    public int ItemsPerChangeSet { get; set; }

    public int ChangeSetsPerDay { get; set; }
}
