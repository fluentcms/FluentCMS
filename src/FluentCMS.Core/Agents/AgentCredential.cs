using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Agents;

// One agent's own credential (MCP-05, SEC-01, ADR-0009): who it is, what it may touch and how much it may do, until it expires
// or is revoked. It never holds its key; only the key's hash is stored, beside it (AgentKeys).
public sealed class AgentCredential
{
    // The audit log and change sets store the display name as the initiator's name, in columns of this length.
    public const int MaxDisplayNameLength = 256;

    private AgentCredential(
        Guid id,
        string displayName,
        AgentScope scope,
        AgentCaps caps,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt)
    {
        Id = id;
        DisplayName = displayName;
        Scope = scope;
        Caps = caps;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        RevokedAt = revokedAt;
    }

    public Guid Id { get; }

    public string DisplayName { get; }

    public AgentScope Scope { get; }

    public AgentCaps Caps { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    // How the audit log names whatever this credential does (AUD-01).
    public AgentInitiator Initiator => new(Id, DisplayName);

    public static Result<AgentCredential> Issue(NewAgentCredential credential, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(credential.Scope);
        ArgumentNullException.ThrowIfNull(credential.Caps);

        var errors = new List<Error>();
        var displayName = credential.DisplayName?.Trim() ?? string.Empty;

        if (displayName.Length == 0)
        {
            errors.Add(new Error(
                AgentCredentialErrorCodes.DisplayNameMissing,
                "A credential needs a display name, which the audit log shows for everything the agent does."));
        }
        else if (displayName.Length > MaxDisplayNameLength)
        {
            errors.Add(new Error(
                AgentCredentialErrorCodes.DisplayNameTooLong,
                $"A display name is at most {MaxDisplayNameLength} characters, and this one has {displayName.Length}."));
        }

        if (credential.ExpiresAt <= now)
        {
            errors.Add(new Error(
                AgentCredentialErrorCodes.ExpiryNotInFuture,
                $"A credential expires after it is issued, and {credential.ExpiresAt:O} is not after {now:O}."));
        }

        return errors.Count == 0
            ? Result<AgentCredential>.Success(new AgentCredential(
                Guid.CreateVersion7(now),
                displayName,
                credential.Scope,
                credential.Caps,
                now.ToUniversalTime(),
                credential.ExpiresAt.ToUniversalTime(),
                revokedAt: null))
            : Result<AgentCredential>.Failure(errors);
    }

    // Rebuilds a stored credential without checking it again, so it stays readable if the rules above change.
    public static AgentCredential Restore(
        Guid id,
        string displayName,
        AgentScope scope,
        AgentCaps caps,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(caps);

        return new AgentCredential(id, displayName, scope, caps, issuedAt, expiresAt, revokedAt);
    }

    // An expired credential can still be revoked, so the list shows it was withdrawn on purpose.
    public Result Revoke(DateTimeOffset now)
    {
        if (RevokedAt is { } revokedAt)
        {
            return Result.Failure(
                AgentCredentialErrorCodes.AlreadyRevoked,
                $"The credential '{DisplayName}' was already revoked at {revokedAt:O}.");
        }

        RevokedAt = now.ToUniversalTime();
        return Result.Success();
    }

    // Whether a presented key for this credential is accepted now. Revocation is reported first, since it is final.
    public Result CheckActive(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return Result.Failure(
                AgentCredentialErrorCodes.Revoked,
                $"The credential '{DisplayName}' has been revoked; ask an administrator for a new key.");
        }

        if (now >= ExpiresAt)
        {
            return Result.Failure(
                AgentCredentialErrorCodes.Expired,
                $"The credential '{DisplayName}' expired at {ExpiresAt:O}; ask an administrator for a new key.");
        }

        return Result.Success();
    }
}
