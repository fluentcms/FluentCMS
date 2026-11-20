using System.Security.Claims;
using System.Text.Json;
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
    // The property of an authorization that names the server session, the sign-in cookie, it was issued in.
    public const string ServerSessionProperty = "fcms_server_session";

    // Set on an authorization issued from a sign-in with the second factor (amr=mfa).
    public const string MultiFactorProperty = "fcms_mfa";

    // The ad-hoc authorization that OpenIddict would make for a code of the client, marked with the server session the
    // user signed in with, so that the logout of that session finds it, and with whether that sign-in had the second
    // factor. Returns its id.
    public async Task<string> BeginAsync(ClaimsPrincipal principal, object application, string serverSession, CancellationToken cancellationToken = default)
    {
        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await applications.GetIdAsync(application, cancellationToken),
            CreationDate = time.GetUtcNow(),
            Principal = principal,
            Status = Statuses.Valid,
            Subject = principal.GetClaim(Claims.Subject),
            Type = AuthorizationTypes.AdHoc,
        };
        descriptor.Scopes.UnionWith(principal.GetScopes());
        descriptor.Properties[ServerSessionProperty] = JsonSerializer.SerializeToElement(serverSession);
        if (principal.HasClaim(Claims.AuthenticationMethodReference, TokenClaims.MultiFactor))
        {
            descriptor.Properties[MultiFactorProperty] = JsonSerializer.SerializeToElement(true);
        }

        var authorization = await authorizations.CreateAsync(descriptor, cancellationToken);
        return await authorizations.GetIdAsync(authorization, cancellationToken)
            ?? throw new InvalidOperationException("The authorization was stored without an id.");
    }

    // The end of a server session of the user: every valid authorization of the user issued in it, with its tokens. The
    // user's other sessions, and a consent, which is no session's, stay. Without a session id, as for a sign-in from
    // before sessions were marked, nothing ends here.
    public Task EndAsync(string? userId, string? serverSession, CancellationToken cancellationToken = default) =>
        EndAsync(userId, serverSession, singleFactorOnly: false, cancellationToken);

    // A sign-in with the second factor in a server session ends what the session was issued without it (#3228), so that a
    // role that has come to require two-factor holds for the session's access tokens from then on, not only once they
    // expire. What was issued with the second factor stays, as do the user's other sessions.
    // A client with RequireConsent has its codes issued under the consent, so the session's tokens of it are found by the
    // mark SingleFactorTokenHandler gives them and revoked on their own, the consent left.
    public async Task EndSingleFactorAsync(string? userId, string? serverSession, CancellationToken cancellationToken = default)
    {
        await EndAsync(userId, serverSession, singleFactorOnly: true, cancellationToken);
        if (string.IsNullOrEmpty(serverSession) || !Guid.TryParse(userId, out var user))
        {
            return;
        }

        List<string> consents = [];
        await foreach (var authorization in authorizations.FindBySubjectAsync(user.ToString("D"), cancellationToken))
        {
            if (await authorizations.HasTypeAsync(authorization, AuthorizationTypes.Permanent, cancellationToken)
                && await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken)
                && await authorizations.GetIdAsync(authorization, cancellationToken) is { } id)
            {
                consents.Add(id);
            }
        }

        foreach (var consent in consents)
        {
            List<object> marked = [];
            await foreach (var token in tokens.FindByAuthorizationIdAsync(consent, cancellationToken))
            {
                if (await tokens.HasStatusAsync(token, Statuses.Valid, cancellationToken)
                    && (await tokens.GetPropertiesAsync(token, cancellationToken)).TryGetValue(ServerSessionProperty, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() == serverSession)
                {
                    marked.Add(token);
                }
            }

            foreach (var token in marked)
            {
                await tokens.TryRevokeAsync(token, cancellationToken);
            }
        }
    }

    private async Task EndAsync(string? userId, string? serverSession, bool singleFactorOnly, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(serverSession) || !Guid.TryParse(userId, out var user))
        {
            return;
        }

        List<string> ids = [];
        await foreach (var authorization in authorizations.FindBySubjectAsync(user.ToString("D"), cancellationToken))
        {
            if (!await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken))
            {
                continue;
            }

            var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken);
            if (properties.TryGetValue(ServerSessionProperty, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() == serverSession
                && !(singleFactorOnly && properties.TryGetValue(MultiFactorProperty, out var multiFactor) && multiFactor.ValueKind == JsonValueKind.True)
                && await authorizations.GetIdAsync(authorization, cancellationToken) is { } id)
            {
                ids.Add(id);
            }
        }

        foreach (var id in ids)
        {
            await RevokeAsync(user, Guid.Parse(id), cancellationToken);
        }
    }

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
