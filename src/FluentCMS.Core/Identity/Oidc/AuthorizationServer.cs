using FluentCMS.Core.Authorization;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

internal static class AuthorizationServer
{
    // Admin users and service accounts; members get the members audience once the member pool issues tokens.
    public const string ManagementAudience = "fluentcms:management";
    public const string MembersAudience = "fluentcms:members";

    // The application property that holds the role names of a service account.
    public const string RolesProperty = "fcms_roles";

    public static readonly string[] KnownScopes =
    [
        TokenScopes.Draft,
        Scopes.Email,
        TokenScopes.Management,
        Scopes.OfflineAccess,
        Scopes.OpenId,
        Scopes.Profile,
    ];
}
