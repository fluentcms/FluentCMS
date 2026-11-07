using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// A session is one sign-in of a user through a client: an OpenIddict authorization, with the refresh tokens that came from it.
internal sealed record UserSession(Guid Id, string? ClientId, string? ClientName, DateTimeOffset? CreatedAt, IReadOnlyList<string> Scopes);

// The sessions of a user, for the Management API (#2786): the valid authorizations that still hold a token in use, so one
// whose tokens have all expired is over even before the prune job removes it. Revoking one revokes its authorization and
// every token issued under it, so its refresh token is refused at once, and so is an access token on the next request.
internal sealed class UserSessions(IOpenIddictAuthorizationManager authorizations, IOpenIddictApplicationManager applications, IOpenIddictTokenManager tokens, TimeProvider time)
{
    public async Task<IReadOnlyList<UserSession>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<UserSession> sessions = [];
        await foreach (var authorization in authorizations.FindBySubjectAsync(userId.ToString("D"), cancellationToken))
        {
            if (await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken)
                && await HasLiveTokenAsync(authorization, cancellationToken))
            {
                sessions.Add(await DescribeAsync(authorization, cancellationToken));
            }
        }

        return [.. sessions.OrderByDescending(session => session.CreatedAt)];
    }

    // False when the user has no such session, or it was revoked already.
    public async Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var authorization = await authorizations.FindByIdAsync(sessionId.ToString("D"), cancellationToken);
        if (authorization is null
            || await authorizations.GetSubjectAsync(authorization, cancellationToken) != userId.ToString("D")
            || !await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken))
        {
            return false;
        }

        var revoked = await authorizations.TryRevokeAsync(authorization, cancellationToken);
        await tokens.RevokeByAuthorizationIdAsync(sessionId.ToString("D"), cancellationToken);
        return revoked;
    }

    // Every valid session of the user, with its tokens, so that its access tokens stop at once too.
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        List<string> ids = [];
        await foreach (var authorization in authorizations.FindBySubjectAsync(userId.ToString("D"), cancellationToken))
        {
            if (await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken)
                && await authorizations.GetIdAsync(authorization, cancellationToken) is { } id)
            {
                ids.Add(id);
            }
        }

        foreach (var id in ids)
        {
            await RevokeAsync(userId, Guid.Parse(id), cancellationToken);
        }
    }

    private async Task<bool> HasLiveTokenAsync(object authorization, CancellationToken cancellationToken)
    {
        var id = await authorizations.GetIdAsync(authorization, cancellationToken) ?? "";
        var now = time.GetUtcNow();
        await foreach (var token in tokens.FindByAuthorizationIdAsync(id, cancellationToken))
        {
            if (!await tokens.HasStatusAsync(token, Statuses.Valid, cancellationToken))
            {
                continue;
            }

            var expires = await tokens.GetExpirationDateAsync(token, cancellationToken);
            if (expires is null || expires > now)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<UserSession> DescribeAsync(object authorization, CancellationToken cancellationToken)
    {
        var id = Guid.Parse(await authorizations.GetIdAsync(authorization, cancellationToken) ?? "");
        string? clientId = null;
        string? clientName = null;
        if (await authorizations.GetApplicationIdAsync(authorization, cancellationToken) is { } applicationId
            && await applications.FindByIdAsync(applicationId, cancellationToken) is { } application)
        {
            clientId = await applications.GetClientIdAsync(application, cancellationToken);
            clientName = await applications.GetDisplayNameAsync(application, cancellationToken);
        }

        var scopes = await authorizations.GetScopesAsync(authorization, cancellationToken);
        return new UserSession(id, clientId, clientName, await authorizations.GetCreationDateAsync(authorization, cancellationToken), [.. scopes]);
    }
}
