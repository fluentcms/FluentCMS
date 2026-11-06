using System.Collections.Immutable;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// What the consent page shows: the client and the scopes of the request that it may ask for.
internal sealed record ConsentRequest(string ClientId, string ClientName, IReadOnlyList<string> Scopes);

// A user's consent to a third-party client (ClientRegistration.RequireConsent) is a permanent OpenIddict authorization of
// the user for the client and the scopes allowed, which the authorize endpoint then issues the client's codes under. Revoking
// it as a session (UserSessions) takes the consent with it.
internal sealed class ClientConsents(IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations)
{
    // The parameter the consent page adds to the authorize request when the user refuses.
    public const string DecisionParameter = "fcms_consent";
    public const string Denied = "denied";

    // Null for a client that does not ask for consent, or does not exist.
    public async Task<ConsentRequest?> DescribeAsync(string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        ArgumentNullException.ThrowIfNull(scopes);

        var application = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null || !await applications.HasConsentTypeAsync(application, ConsentTypes.Explicit, cancellationToken))
        {
            return null;
        }

        List<string> allowed = [];
        foreach (var scope in scopes.Distinct(StringComparer.Ordinal))
        {
            if (scope == Scopes.OpenId || await applications.HasPermissionAsync(application, Permissions.Prefixes.Scope + scope, cancellationToken))
            {
                allowed.Add(scope);
            }
        }

        return new ConsentRequest(clientId, await applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId, allowed);
    }

    // The user's valid consent to the client that covers every scope, or null.
    public async Task<object?> FindAsync(Guid userId, string applicationId, ImmutableArray<string> scopes, CancellationToken cancellationToken = default)
    {
        await foreach (var authorization in authorizations.FindAsync(userId.ToString("D"), applicationId, Statuses.Valid, AuthorizationTypes.Permanent, scopes, cancellationToken))
        {
            return authorization;
        }

        return null;
    }

    // Records that the user allows the client the scopes it may ask for among these; false for a client that asks no consent.
    public async Task<bool> GrantAsync(Guid userId, string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var request = await DescribeAsync(clientId, scopes, cancellationToken);
        if (request is null || await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
        {
            return false;
        }

        var subject = userId.ToString("D");
        var identity = TokenClaims.Identity().SetClaim(Claims.Subject, subject);
        await authorizations.CreateAsync(
            identity,
            subject,
            await applications.GetIdAsync(application, cancellationToken) ?? "",
            AuthorizationTypes.Permanent,
            [.. request.Scopes],
            cancellationToken);
        return true;
    }
}
