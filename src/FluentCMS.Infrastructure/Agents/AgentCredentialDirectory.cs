using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Agents;

// Expected failures are found before the unit of work begins, so they leave a caller's outer unit of work able to commit.
// Reads never track rows, and revocation goes straight to the database, so a long-lived context always sees what is stored.
internal sealed class AgentCredentialDirectory(
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    TimeProvider clock) : IAgentCredentialDirectory
{
    private const string SubjectType = "agent_credential";

    // One answer for every key that matches no credential, so the message never echoes what was presented.
    private static readonly Result<AgentCredential> KeyInvalid = Result<AgentCredential>.Failure(
        AgentCredentialErrorCodes.KeyInvalid,
        "The key is not a valid agent key; check that it was copied whole, or ask an administrator for a new one.");

    public async Task<IReadOnlyList<AgentCredential>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.AgentCredentials
            .AsNoTracking()
            .OrderBy(row => row.DisplayName)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToCredential).ToList();
    }

    public async Task<Result<AgentCredential>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(id, cancellationToken);

        return row is null ? NotFound(id) : Result<AgentCredential>.Success(ToCredential(row));
    }

    public async Task<Result<IssuedAgentCredential>> IssueAsync(NewAgentCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (RefuseAgent() is { } refused)
        {
            return Result<IssuedAgentCredential>.Failure(refused.Errors);
        }

        var issued = AgentCredential.Issue(credential, clock.GetUtcNow());
        if (!issued.IsSuccess)
        {
            return Result<IssuedAgentCredential>.Failure(issued.Errors);
        }

        var sites = credential.Scope.Sites;
        var known = await context.Sites.AsNoTracking().Where(site => sites.Contains(site.Id)).Select(site => site.Id).ToListAsync(cancellationToken);
        var unknown = sites.Except(known).ToList();
        if (unknown.Count > 0)
        {
            return Result<IssuedAgentCredential>.Failure(unknown.Select(site => new Error(
                AgentCredentialErrorCodes.ScopeSiteUnknown,
                $"There is no site {site} to put in the scope.")));
        }

        var agent = issued.Value;
        var key = AgentKeys.Generate();

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        context.AgentCredentials.Add(ToRow(agent, AgentKeys.Hash(key)));
        await auditLog.AppendAsync(new NewAuditEvent("agent_credential.issued") { SubjectType = SubjectType, SubjectId = agent.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<IssuedAgentCredential>.Success(new IssuedAgentCredential(agent, key));
    }

    public async Task<Result<AgentCredential>> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (RefuseAgent() is { } refused)
        {
            return Result<AgentCredential>.Failure(refused.Errors);
        }

        var row = await FindAsync(id, cancellationToken);
        if (row is null)
        {
            return NotFound(id);
        }

        var credential = ToCredential(row);
        var revoked = credential.Revoke(clock.GetUtcNow());
        if (!revoked.IsSuccess)
        {
            return Result<AgentCredential>.Failure(revoked.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        // Only a credential nobody revoked in the meantime is updated, so of two concurrent revokes the second changes nothing.
        var updated = await context.AgentCredentials
            .Where(stored => stored.Id == id && stored.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(stored => stored.RevokedAt, credential.RevokedAt), cancellationToken);
        if (updated == 0)
        {
            return Result<AgentCredential>.Failure(
                AgentCredentialErrorCodes.AlreadyRevoked,
                $"The credential '{credential.DisplayName}' was revoked by another call at the same time.");
        }

        await auditLog.AppendAsync(new NewAuditEvent("agent_credential.revoked") { SubjectType = SubjectType, SubjectId = id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);

        return Result<AgentCredential>.Success(credential);
    }

    public async Task<Result<AgentCredential>> AuthenticateAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!AgentKeys.IsWellFormed(key))
        {
            return KeyInvalid;
        }

        var hash = AgentKeys.Hash(key);
        var row = await context.AgentCredentials.AsNoTracking().SingleOrDefaultAsync(stored => stored.KeyHash == hash, cancellationToken);
        if (row is null)
        {
            return KeyInvalid;
        }

        var credential = ToCredential(row);
        var active = credential.CheckActive(clock.GetUtcNow());

        return active.IsSuccess ? Result<AgentCredential>.Success(credential) : Result<AgentCredential>.Failure(active.Errors);
    }

    // A credential grants permissions, which only a person changes: no agent, whatever its own scope (SEC-02), and no engine.
    private Result? RefuseAgent()
    {
        return currentInitiator.Current is AgentInitiator or SystemInitiator
            ? Result.Failure(AgentCredentialErrorCodes.PeopleOnly, "Only a person can issue or revoke an agent credential; an agent cannot.")
            : null;
    }

    private Task<AgentCredentialRow?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.AgentCredentials.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
    }

    private static AgentCredentialRow ToRow(AgentCredential credential, byte[] keyHash)
    {
        return new AgentCredentialRow
        {
            Id = credential.Id,
            DisplayName = credential.DisplayName,
            KeyHash = keyHash,
            IssuedAt = credential.IssuedAt,
            ExpiresAt = credential.ExpiresAt,
            RevokedAt = credential.RevokedAt,
            Sites = [.. credential.Scope.Sites],
            Types = credential.Scope.Types?.ToList(),
            Fields = credential.Scope.Fields?.ToList(),
            Operations = [.. credential.Scope.Operations],
            CallsPerPeriod = credential.Caps.CallsPerPeriod,
            CallPeriod = credential.Caps.CallPeriod,
            LlmSpendPerPeriod = credential.Caps.LlmSpendPerPeriod,
            LlmSpendPeriod = credential.Caps.LlmSpendPeriod,
            ItemsPerChangeSet = credential.Caps.ItemsPerChangeSet,
            ChangeSetsPerDay = credential.Caps.ChangeSetsPerDay,
        };
    }

    private static AgentCredential ToCredential(AgentCredentialRow row)
    {
        return AgentCredential.Restore(
            row.Id,
            row.DisplayName,
            AgentScope.Restore(row.Sites, row.Types, row.Fields, row.Operations),
            AgentCaps.Restore(row.CallsPerPeriod, row.CallPeriod, row.LlmSpendPerPeriod, row.LlmSpendPeriod, row.ItemsPerChangeSet, row.ChangeSetsPerDay),
            row.IssuedAt,
            row.ExpiresAt,
            row.RevokedAt);
    }

    private static Result<AgentCredential> NotFound(Guid id)
    {
        return Result<AgentCredential>.Failure(AgentCredentialErrorCodes.CredentialNotFound, $"There is no agent credential {id}.");
    }
}
