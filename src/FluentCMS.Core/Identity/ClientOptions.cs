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

    // The client of the admin application, whose requests run with Origin.Admin.
    public string AdminClientId { get; set; } = DefaultAdminClientId;

    public IDictionary<string, ClientRegistration> Applications { get; } = new Dictionary<string, ClientRegistration>(StringComparer.Ordinal);

    internal void Validate()
    {
        Require(!string.IsNullOrWhiteSpace(AdminClientId), $"{nameof(AdminClientId)} must not be empty.");

        foreach (var (clientId, client) in Applications)
        {
            var key = $"{nameof(Applications)}:{clientId}";
            Require(!string.IsNullOrWhiteSpace(clientId) && clientId.Length <= ClientIdLength && !clientId.Any(char.IsControl), $"{key} is not a client id of 1 to {ClientIdLength} characters.");
            Require(client.IsConfidential || client.RedirectUris.Count > 0, $"{key} has neither a {nameof(ClientRegistration.Secret)} nor {nameof(ClientRegistration.RedirectUris)}; a public client signs users in through a redirect URI.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FluentCmsConfigurationException(OidcErrorCodes.InvalidClients, message);
        }
    }
}

// A client with a secret is confidential: it may sign in as a service account with client credentials, holding the
// instance-wide roles Roles names. A client with redirect URIs signs users in with the authorization code flow, PKCE and
// refresh tokens. A client may be both.
public sealed class ClientRegistration
{
    public string? DisplayName { get; set; }

    public string? Secret { get; set; }

    public IList<string> RedirectUris { get; } = [];

    public IList<string> PostLogoutRedirectUris { get; } = [];

    // What the client may ask for; a token holds the ones it asked for.
    public IList<string> Scopes { get; } = [];

    public IList<string> Roles { get; } = [];

    internal bool IsConfidential => !string.IsNullOrEmpty(Secret);
}
