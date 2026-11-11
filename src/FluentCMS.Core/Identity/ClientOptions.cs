using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Oidc;

namespace FluentCMS.Core.Identity;

// cms.Clients, and FluentCms:Clients in configuration: the OAuth clients of the authorization server, keyed by client id
// and written to cms_oidc_application when the host starts. A client that is not listed is left as it is.
public sealed class ClientOptions
{
    public const string DefaultAdminClientId = "fluentcms-admin";

    internal const string Section = "Clients";

    // OpenIddict's column for a client id is this long.
    private const int ClientIdLength = 100;

    // The longest lifetime, which also keeps the expiry dates worked out from one in range.
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(365);

    // The client of the admin application, whose requests run with Origin.Admin.
    public string AdminClientId { get; set; } = DefaultAdminClientId;

    // OpenIddict's defaults. A refresh token is good for its lifetime if it is not used, and using one gives a new one.
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    // Identity's default. The sign-in cookie of the hosted pages lasts this long unused, and each use slides it on.
    public TimeSpan SignInCookieLifetime { get; set; } = TimeSpan.FromDays(14);

    // How long a session lasts after the user signed in, however often its refresh token or the cookie is used; past it the
    // token endpoint refuses the refresh and the authorize endpoint asks the user to sign in again. Null sets no limit.
    public TimeSpan? AbsoluteSessionLifetime { get; set; }

    public IDictionary<string, ClientRegistration> Applications { get; } = new Dictionary<string, ClientRegistration>(StringComparer.Ordinal);

    internal void Validate()
    {
        Require(!string.IsNullOrWhiteSpace(AdminClientId), $"{nameof(AdminClientId)} must not be empty.");
        RequireLifetime(AccessTokenLifetime, nameof(AccessTokenLifetime));
        RequireLifetime(RefreshTokenLifetime, nameof(RefreshTokenLifetime));
        RequireLifetime(SignInCookieLifetime, nameof(SignInCookieLifetime));
        if (AbsoluteSessionLifetime is { } absolute)
        {
            RequireLifetime(absolute, nameof(AbsoluteSessionLifetime));
        }

        Require(AccessTokenLifetime <= RefreshTokenLifetime, $"{nameof(AccessTokenLifetime)} is longer than {nameof(RefreshTokenLifetime)}, so a client would refresh a token that outlives the refresh token.");

        Require(!Applications.TryGetValue(AdminClientId, out var adminClient) || adminClient.Pool == ClientPool.Admins, $"{nameof(AdminClientId)} is '{AdminClientId}', a client of the member pool; the admin application signs admin users in.");

        foreach (var (clientId, client) in Applications)
        {
            var key = $"{nameof(Applications)}:{clientId}";
            Require(!string.IsNullOrWhiteSpace(clientId) && clientId.Length <= ClientIdLength && !clientId.Any(char.IsControl), $"{key} is not a client id of 1 to {ClientIdLength} characters.");
            if (client.Pool == ClientPool.Members)
            {
                Require(client.RedirectUris.Count > 0 || client.AllowDirectLogin, $"{key} has neither {nameof(ClientRegistration.RedirectUris)} nor {nameof(ClientRegistration.AllowDirectLogin)}; a client of the member pool signs members in through a redirect URI or the direct login.");
                Require(client.Roles.Count == 0, $"{key}:{nameof(ClientRegistration.Roles)} is set on a client of the member pool; it signs members in and is never a service account.");
                Require(!client.RequireConsent, $"{key}:{nameof(ClientRegistration.RequireConsent)} is set on a client of the member pool, whose pages have no consent step.");
                foreach (var scope in client.Scopes)
                {
                    Require(AuthorizationServer.MemberScopes.Contains(scope, StringComparer.Ordinal), $"{key}:{nameof(ClientRegistration.Scopes)} has '{scope}'; a client of the member pool asks for {string.Join(", ", AuthorizationServer.MemberScopes)} only.");
                }
            }
            else
            {
                Require(!client.AllowDirectLogin, $"{key}:{nameof(ClientRegistration.AllowDirectLogin)} is set on a client of the admin pool; the direct login is for members, so set {nameof(ClientRegistration.Pool)} to {nameof(ClientPool.Members)}.");
                Require(client.IsConfidential || client.RedirectUris.Count > 0, $"{key} has neither a {nameof(ClientRegistration.Secret)} nor {nameof(ClientRegistration.RedirectUris)}; a public client signs users in through a redirect URI.");
            }

            Require(client.Roles.Count == 0 || client.IsConfidential, $"{key}:{nameof(ClientRegistration.Roles)} is set on a client without a {nameof(ClientRegistration.Secret)}; only a confidential client signs in as a service account.");

            foreach (var uri in client.RedirectUris.Concat(client.PostLogoutRedirectUris))
            {
                Require(Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && string.IsNullOrEmpty(parsed.Fragment), $"{key} has the redirect URI '{uri}'; it must be absolute and have no fragment.");
            }

            foreach (var scope in client.Scopes)
            {
                Require(AuthorizationServer.KnownScopes.Contains(scope, StringComparer.Ordinal), $"{key}:{nameof(ClientRegistration.Scopes)} has '{scope}'; the scopes are {string.Join(", ", AuthorizationServer.KnownScopes)}.");
            }
        }
    }

    private static void RequireLifetime(TimeSpan lifetime, string name) =>
        Require(lifetime > TimeSpan.Zero && lifetime <= MaximumLifetime, $"{name} is {lifetime}; a lifetime is more than zero and at most {MaximumLifetime.TotalDays:0} days.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(OidcErrorCodes.InvalidClients, message);
        }
    }
}

// Which pool a client signs in: the admin users, or the members.
public enum ClientPool
{
    Admins = 0,
    Members = 1,
}

// A client with a secret is confidential: it may sign in as a service account with client credentials, holding the
// instance-wide roles Roles names. A client with redirect URIs signs users in with the authorization code flow, PKCE and
// refresh tokens. A client may be both. A client of the member pool signs members in and nothing else: its tokens have
// the members audience and no scope beyond openid, profile, email and offline_access, and it is never a service account.
public sealed class ClientRegistration
{
    public string? DisplayName { get; set; }

    public string? Secret { get; set; }

    public IList<string> RedirectUris { get; } = [];

    public IList<string> PostLogoutRedirectUris { get; } = [];

    // What the client may ask for; a token holds the ones it asked for.
    public IList<string> Scopes { get; } = [];

    public IList<string> Roles { get; } = [];

    // A third-party client: the user allows it on the consent page before it gets a code, once for the scopes it asks.
    public bool RequireConsent { get; set; }

    // The pool the client signs in. The authorize endpoint asks a client of the member pool for the member cookie, and
    // one of the admin pool for the admin's.
    public ClientPool Pool { get; set; }

    // Lets a client of the member pool send a member's address and password to the token endpoint, while
    // cms.Members.AllowDirectLogin is on too. For first-party apps only: the app sees the password.
    public bool AllowDirectLogin { get; set; }

    internal bool IsConfidential => !string.IsNullOrEmpty(Secret);
}
