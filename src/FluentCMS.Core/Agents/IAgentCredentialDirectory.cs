using FluentCMS.Core.Results;

namespace FluentCMS.Core.Agents;

// The credentials agents present as bearer keys (MCP-05, SEC-01). Issuing and revoking each append their audit event in the same
// unit of work (AUD-N1), and only a person may do either: a credential grants permissions, which no agent changes (SEC-02).
public interface IAgentCredentialDirectory
{
    Task<IReadOnlyList<AgentCredential>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<AgentCredential>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // The key is in the result and nowhere else. Only its hash is stored, so it cannot be shown again.
    Task<Result<IssuedAgentCredential>> IssueAsync(NewAgentCredential credential, CancellationToken cancellationToken = default);

    Task<Result<AgentCredential>> RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    // The credential a presented key belongs to, refused once it is expired or revoked. A read, so it appends no event.
    Task<Result<AgentCredential>> AuthenticateAsync(string key, CancellationToken cancellationToken = default);
}
