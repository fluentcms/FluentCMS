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

    // Marks a client cms.Clients registered, which is deleted once the options no longer list it.
    public const string FromOptionsProperty = "fcms_from_options";

    // The scopes that say which surface a token is for, one of which a protected resource of a plugin asks for.
    public static readonly string[] ResourceScopes = [TokenScopes.Mcp];

    public static readonly string[] KnownScopes =
    [
        TokenScopes.Draft,
        Scopes.Email,
        TokenScopes.Management,
        TokenScopes.Mcp,
        Scopes.OfflineAccess,
        Scopes.OpenId,
        Scopes.Profile,
    ];
}
