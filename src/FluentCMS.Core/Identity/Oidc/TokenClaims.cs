using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Validation.OpenIddictValidationEvents;

namespace FluentCMS.Core.Identity.Oidc;

// The claims of FluentCMS tokens, on the way out and on the way in. A token carries sub, name, fcms_kind and, for a
// service account, the roles of its client; the user's roles are read from cms_user_role on each request instead. A user's
// tokens carry amr, how the user signed in on the hosted pages.
internal static class TokenClaims
{
    // Kept in codes and refresh tokens only, which the server alone reads, so that a password change or reset ends them.
    public const string SecurityStamp = "fcms_security_stamp";

    // The authentication method reference Identity gives a sign-in with a second factor.
    public const string MultiFactor = "mfa";

    // A new identity, or one that keeps what a code or refresh token carried: its scopes, audiences and session.
    public static ClaimsIdentity Identity(ClaimsPrincipal? from = null) =>
        new(from?.Claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, CmsClaimTypes.Roles);

    // Without methods, a code or refresh token keeps those it carried.
    public static ClaimsIdentity ForUser(ClaimsIdentity identity, CmsUser user, string? securityStamp, IEnumerable<string> scopes, IEnumerable<string>? methods = null)
    {
        identity.SetClaim(Claims.Subject, user.Id.ToString("D"))
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(SecurityStamp, securityStamp)
            .SetClaim(CmsClaimTypes.Kind, nameof(PrincipalKind.AdminUser));
        if (methods is not null)
        {
            identity.SetClaims(Claims.AuthenticationMethodReference, [.. methods.Distinct(StringComparer.Ordinal)]);
        }

        identity.SetScopes(scopes);
        identity.SetAudiences(AuthorizationServer.ManagementAudience);
        identity.SetDestinations(Destinations);
        return identity;
    }

    public static ClaimsIdentity ForServiceAccount(ClaimsIdentity identity, Guid applicationId, string name, IEnumerable<Guid> roles, IEnumerable<string> scopes)
    {
        identity.SetClaim(Claims.Subject, applicationId.ToString("D"))
            .SetClaim(Claims.Name, name)
            .SetClaim(CmsClaimTypes.Kind, nameof(PrincipalKind.ServiceAccount));
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(CmsClaimTypes.Roles, role.ToString("D")));
        }

        identity.SetScopes(scopes);
        identity.SetAudiences(AuthorizationServer.ManagementAudience);
        identity.SetDestinations(Destinations);
        return identity;
    }

    // What a validated access token becomes on a request: the claims the request bridge reads and nothing of OpenIddict's.
    public static ClaimsPrincipal ForRequest(ClaimsPrincipal token)
    {
        var identity = new ClaimsIdentity(token.Identity?.AuthenticationType ?? "Bearer", CmsClaimTypes.Name, CmsClaimTypes.Roles);
        Add(identity, CmsClaimTypes.Subject, token.GetClaim(Claims.Subject));
        Add(identity, CmsClaimTypes.Name, token.GetClaim(Claims.Name));
        Add(identity, CmsClaimTypes.Kind, token.GetClaim(CmsClaimTypes.Kind));
        Add(identity, CmsClaimTypes.ClientId, token.GetClaim(Claims.ClientId) ?? token.GetPresenters().FirstOrDefault());

        var scopes = token.GetScopes();
        Add(identity, CmsClaimTypes.Scope, scopes.IsDefaultOrEmpty ? null : string.Join(' ', scopes));

        foreach (var role in token.GetClaims(CmsClaimTypes.Roles))
        {
            identity.AddClaim(new Claim(CmsClaimTypes.Roles, role));
        }

        foreach (var method in token.GetClaims(Claims.AuthenticationMethodReference))
        {
            identity.AddClaim(new Claim(CmsClaimTypes.AuthenticationMethods, method));
        }

        return new ClaimsPrincipal(identity);
    }

    // RequestPrincipal joins the values of a claim with spaces.
    public static bool SignedInWithSecondFactor(Principal principal) =>
        principal.Claim(CmsClaimTypes.AuthenticationMethods)?.Split(' ').Contains(MultiFactor, StringComparer.Ordinal) == true;

    private static IEnumerable<string> Destinations(Claim claim) => claim.Type switch
    {
        Claims.Name when claim.Subject?.HasScope(Scopes.Profile) == true => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true => [OpenIddictConstants.Destinations.IdentityToken],
        Claims.AuthenticationMethodReference => [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
        Claims.Name or CmsClaimTypes.Kind or CmsClaimTypes.Roles => [OpenIddictConstants.Destinations.AccessToken],
        _ => [],
    };

    private static void Add(ClaimsIdentity identity, string type, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }
}

// Replaces the principal of a validated access token with the one TokenClaims makes of it, once OpenIddict has checked it.
internal sealed class BearerClaimsHandler : IOpenIddictValidationHandler<ProcessAuthenticationContext>
{
    public static OpenIddictValidationHandlerDescriptor Descriptor { get; } =
        OpenIddictValidationHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
            .UseSingletonHandler<BearerClaimsHandler>()
            .SetOrder(OpenIddictValidationHandlers.ValidateAccessToken.Descriptor.Order + 1_000)
            .SetType(OpenIddictValidationHandlerType.Custom)
            .Build();

    public ValueTask HandleAsync(ProcessAuthenticationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AccessTokenPrincipal is { } principal)
        {
            context.AccessTokenPrincipal = TokenClaims.ForRequest(principal);
        }

        return ValueTask.CompletedTask;
    }
}
