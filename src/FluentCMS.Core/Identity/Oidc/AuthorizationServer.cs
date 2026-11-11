using FluentCMS.Core.Authorization;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

internal static class AuthorizationServer
{
    // Admin users and service accounts hold the first, members the second; the bearer scheme accepts both, and what a
    // token may do is held by its scopes.
    public const string ManagementAudience = "fluentcms:management";
    public const string MembersAudience = "fluentcms:members";

    // The custom grant of the direct login (Q4): a member's address and password sent to the token endpoint.
    public const string MemberPasswordGrant = "member_password";

    // Marks a client of the member pool, and one of those that may use the direct login.
    public const string MembersProperty = "fcms_members";
    public const string DirectLoginProperty = "fcms_direct_login";

    // The application property that holds the role names of a service account.
    public const string RolesProperty = "fcms_roles";

    // Marks a client cms.Clients registered, which is deleted once the options no longer list it.
    public const string FromOptionsProperty = "fcms_from_options";

    // Marks an MCP client that registered itself, and holds its state: status, role, who approved it and when (McpClientState).
    public const string McpClientProperty = "fcms_mcp";

    // What a client that registered itself may ask for: never management or draft.
    public static readonly string[] RegisteredClientScopes = [TokenScopes.Mcp, Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess];

    // All a member's token ever holds: never management, draft or mcp, which keeps it out of the Management API, of
    // drafts and of /mcp.
    public static readonly string[] MemberScopes = [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess];

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
