using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
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

    // The ad-hoc authorization that OpenIddict would make for a code of the client, marked with the server session the
    // user signed in with, so that the logout of that session finds it. Returns its id.
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

        var authorization = await authorizations.CreateAsync(descriptor, cancellationToken);
        return await authorizations.GetIdAsync(authorization, cancellationToken)
            ?? throw new InvalidOperationException("The authorization was stored without an id.");
    }

    // The logout of a server session, the cookie of the signed-in user whose id is given: every valid authorization of
    // the user issued in it, with its tokens. The user's other sessions, and a consent, which is no session's, stay. A
    // cookie from before sessions were marked ends nothing here.
    public async Task EndAsync(string? userId, AuthenticationProperties cookie, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cookie);

        if (CmsSignInManager.ServerSession(cookie) is not { } serverSession || !Guid.TryParse(userId, out var user))
        {
            return;
        }

        List<string> ids = [];
        await foreach (var authorization in authorizations.FindBySubjectAsync(user.ToString("D"), cancellationToken))
        {
            if (await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken)
                && (await authorizations.GetPropertiesAsync(authorization, cancellationToken)).TryGetValue(ServerSessionProperty, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() == serverSession
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
