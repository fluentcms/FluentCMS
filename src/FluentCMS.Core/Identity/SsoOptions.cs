using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Identity;

// cms.Sso, and FluentCms:Sso in configuration: the external OpenID Connect providers admin users sign in with on the hosted
// login page. A provider's name is its authentication scheme, the login provider of its users in cms_user_login, and part of
// its callback path, /account/sso/{name}. Every preset is the same OpenID Connect handler; a preset only builds the
// authority from what its provider calls it and names the button.
public sealed partial class SsoOptions
{
    internal const string Section = "Sso";

    private const int NameLength = 64;

    // The password form is not shown and a password sign-in is refused, so admin users sign in through a provider only.
    public bool DisableLocalLogin { get; set; }

    public IDictionary<string, SsoProvider> Providers { get; } = new Dictionary<string, SsoProvider>(StringComparer.OrdinalIgnoreCase);

    internal void Validate()
    {
        Require(!DisableLocalLogin || Providers.Count > 0, $"{nameof(DisableLocalLogin)} is set and no provider is configured, so no one could sign in.");

        foreach (var (name, provider) in Providers)
        {
            var key = $"{nameof(Providers)}:{name}";
            Require(Name().IsMatch(name), $"{key}: a provider name is 1 to {NameLength} letters, digits, '-' or '_', since it is part of the callback path.");
            provider.Validate(key);
        }
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(IdentityErrorCodes.InvalidSsoOptions, message);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex Name();
}

public enum SsoPreset
{
    Oidc,
    EntraId,
    Okta,
    Keycloak,
}

public sealed class SsoProvider
{
    // Entra ID's endpoints that take any tenant: their issuer names the user's own tenant, which the handler cannot check.
    private static readonly string[] SharedTenants = ["common", "organizations", "consumers"];

    public SsoPreset Preset { get; set; } = SsoPreset.Oidc;

    // The text of the button, "Sign in with {DisplayName}"; the preset's name, or the provider's for generic OIDC.
    public string? DisplayName { get; set; }

    // The issuer. Required for generic OIDC; for a preset it is built from the fields below unless it is set.
    public string? Authority { get; set; }

    // Entra ID: the directory (tenant) id or a verified domain of it.
    public string? TenantId { get; set; }

    // Okta: the org domain, such as dev-123.okta.com, and the custom authorization server, if one is used.
    public string? Domain { get; set; }

    public string? AuthorizationServerId { get; set; }

    // Keycloak: the server's base URL and the realm.
    public string? BaseUrl { get; set; }

    public string? Realm { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // Asked for after openid, profile and email.
    public IList<string> Scopes { get; } = [];

    // A first sign-in of someone with no user here creates one, with DefaultRole and the roles of RoleMappings.
    public bool AutoProvision { get; set; } = true;

    // A first sign-in whose email address belongs to a user with no login of this provider links that user, when the token
    // says email_verified is true. Off, the address is refused, so that a provider cannot take over an account.
    public bool LinkExistingUsers { get; set; }

    // A role id, or the exact name of an instance-wide role, given to a user this provider creates.
    public string? DefaultRole { get; set; }

    public string EmailClaim { get; set; } = "email";

    public string GroupsClaim { get; set; } = "groups";

    // Each group of the token gives the role mapped to it, at every sign-in, and a role mapped to a group the token no
    // longer has is taken away. Only what is listed is given: no group gives a role by its name.
    public IList<SsoRoleMapping> RoleMappings { get; } = [];

    // Whether the provider's amr claim of mfa counts as the second factor here. Off, a user whose role requires two-factor
    // gives the local one after the provider.
    public bool TrustMfaClaim { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    internal string Name(string name) => DisplayName is { Length: > 0 } display ? display : Preset switch
    {
        SsoPreset.EntraId => "Microsoft Entra ID",
        SsoPreset.Okta => "Okta",
        SsoPreset.Keycloak => "Keycloak",
        _ => name,
    };

    internal string ResolveAuthority()
    {
        if (!string.IsNullOrWhiteSpace(Authority))
        {
            return Authority.Trim();
        }

        return Preset switch
        {
            SsoPreset.EntraId => $"https://login.microsoftonline.com/{Uri.EscapeDataString(TenantId!.Trim())}/v2.0",
            SsoPreset.Okta => $"{Origin(Domain!)}{(string.IsNullOrWhiteSpace(AuthorizationServerId) ? "" : "/oauth2/" + Uri.EscapeDataString(AuthorizationServerId.Trim()))}",
            SsoPreset.Keycloak => $"{BaseUrl!.Trim().TrimEnd('/')}/realms/{Uri.EscapeDataString(Realm!.Trim())}",
            _ => throw new InvalidOperationException("A generic OIDC provider has no authority to build."),
        };
    }

    internal void Validate(string key)
    {
        SsoOptions.Require(Enum.IsDefined(Preset), $"{key}:{nameof(Preset)} is not one of {string.Join(", ", Enum.GetNames<SsoPreset>())}.");
        SsoOptions.Require(!string.IsNullOrWhiteSpace(ClientId), $"{key}:{nameof(ClientId)} is required.");
        SsoOptions.Require(!string.IsNullOrWhiteSpace(EmailClaim), $"{key}:{nameof(EmailClaim)} must not be empty.");
        SsoOptions.Require(!string.IsNullOrWhiteSpace(GroupsClaim), $"{key}:{nameof(GroupsClaim)} must not be empty.");

        if (string.IsNullOrWhiteSpace(Authority))
        {
            switch (Preset)
            {
                case SsoPreset.EntraId:
                    SsoOptions.Require(!string.IsNullOrWhiteSpace(TenantId), $"{key}:{nameof(TenantId)} is required for Entra ID.");
                    SsoOptions.Require(!SharedTenants.Contains(TenantId!.Trim(), StringComparer.OrdinalIgnoreCase), $"{key}:{nameof(TenantId)} is '{TenantId}'; name one tenant, since a shared endpoint signs in users of any tenant.");
                    break;
                case SsoPreset.Okta:
                    SsoOptions.Require(!string.IsNullOrWhiteSpace(Domain), $"{key}:{nameof(Domain)} is required for Okta.");
                    break;
                case SsoPreset.Keycloak:
                    SsoOptions.Require(!string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Realm), $"{key}:{nameof(BaseUrl)} and {nameof(Realm)} are required for Keycloak.");
                    break;
                default:
                    SsoOptions.Require(false, $"{key}:{nameof(Authority)} is required for a generic OIDC provider.");
                    break;
            }
        }

        var authority = Uri.TryCreate(ResolveAuthority(), UriKind.Absolute, out var parsed) ? parsed : null;
        SsoOptions.Require(
            authority is not null && (authority.Scheme == Uri.UriSchemeHttps || (!RequireHttpsMetadata && authority.Scheme == Uri.UriSchemeHttp)) && string.IsNullOrEmpty(authority.Query) && string.IsNullOrEmpty(authority.Fragment),
            $"{key}: the authority '{ResolveAuthority()}' is not an https URL with no query; http needs {nameof(RequireHttpsMetadata)} off.");

        for (var index = 0; index < RoleMappings.Count; index++)
        {
            var mapping = RoleMappings[index];
            SsoOptions.Require(!string.IsNullOrWhiteSpace(mapping.Group) && !string.IsNullOrWhiteSpace(mapping.Role), $"{key}:{nameof(RoleMappings)}:{index} needs a {nameof(SsoRoleMapping.Group)} and a {nameof(SsoRoleMapping.Role)}.");
        }
    }

    private static string Origin(string domain)
    {
        var trimmed = domain.Trim().TrimEnd('/');
        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
    }
}

public sealed class SsoRoleMapping
{
    public string Group { get; set; } = "";

    // A role id, or the exact name of an instance-wide role.
    public string Role { get; set; } = "";
}
