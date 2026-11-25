using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// Writes the clients of cms.Clients to cms_oidc_application when the host starts, after the migrations. A listed client is
// replaced by what the options say, its secret included, unless it is that already. A client the options registered and no
// longer list is deleted with its sessions and tokens, so that dropping a compromised client from the configuration ends it,
// except when the options list none of the operator's own, only those a package contributed or none at all, which is more
// likely a configuration that failed to load. A client registered some other way is left alone.
internal sealed class ClientRegistrationService(IServiceScopeFactory scopes, ClientOptions clients, ILogger<ClientRegistrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        await RemoveUnlistedAsync(applications, cancellationToken);

        foreach (var (clientId, client) in clients.Applications)
        {
            if (await applications.FindByClientIdAsync(clientId, cancellationToken) is { } existing)
            {
                var wanted = Describe(clientId, client);
                if (!await IsCurrentAsync(applications, existing, wanted, cancellationToken))
                {
                    await UpdateAsync(applications, authorizations, existing, wanted, cancellationToken);
                }

                continue;
            }

            try
            {
                await applications.CreateAsync(Describe(clientId, client), cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or OpenIddictExceptions.ValidationException)
            {
                // Another instance registered it first.
                var registered = await applications.FindByClientIdAsync(clientId, cancellationToken);
                if (registered is null)
                {
                    throw;
                }

                await UpdateAsync(applications, authorizations, registered, Describe(clientId, client), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // A consent is of the user, the client and the scopes, and covers every redirect URI the client has, so a client whose
    // redirect URIs change loses its consents, and each user is asked again. A client with explicit consent has its codes
    // issued under the consent itself (ConnectEndpoints), so the refresh and access tokens issued under a consent end
    // with it; the sessions of a client without explicit consent, which has no consent to lose, stay. The consents go
    // before the client is written: a start that fails between the two leaves the old URIs without consents, which the
    // next start still sees as a client to update, where the other order would leave the new URIs with the old consents
    // and a client that looks current. The URIs are compared as Uri values, so a host written in another case, which
    // IsCurrentAsync writes as configured, names the same place and ends no consent.
    private static async Task UpdateAsync(IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations, object application, OpenIddictApplicationDescriptor wanted, CancellationToken cancellationToken)
    {
        var stored = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(stored, application, cancellationToken);
        if (!stored.RedirectUris.SetEquals(wanted.RedirectUris))
        {
            await RevokeConsentsAsync(applications, authorizations, application, cancellationToken);
        }

        await applications.UpdateAsync(application, wanted, cancellationToken);
    }

    private static async Task RevokeConsentsAsync(IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations, object application, CancellationToken cancellationToken)
    {
        var id = await applications.GetIdAsync(application, cancellationToken) ?? "";
        List<object> consents = [];
        await foreach (var authorization in authorizations.FindByApplicationIdAsync(id, cancellationToken))
        {
            if (await authorizations.HasTypeAsync(authorization, AuthorizationTypes.Permanent, cancellationToken)
                && await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken))
            {
                consents.Add(authorization);
            }
        }

        foreach (var consent in consents)
        {
            await authorizations.TryRevokeAsync(consent, cancellationToken);
        }
    }

    private async Task RemoveUnlistedAsync(IOpenIddictApplicationManager applications, CancellationToken cancellationToken)
    {
        List<(object Application, string ClientId)> unlisted = [];
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            var properties = await applications.GetPropertiesAsync(application, cancellationToken);
            if (properties.ContainsKey(AuthorizationServer.FromOptionsProperty)
                && await applications.GetClientIdAsync(application, cancellationToken) is { } clientId
                && !clients.Applications.ContainsKey(clientId))
            {
                unlisted.Add((application, clientId));
            }
        }

        // Read from the clients themselves, so that it holds whatever order the configures and post-configures ran in.
        if (unlisted.Count > 0 && clients.Applications.Values.All(client => client.ContributedByPackage))
        {
            OidcLog.ClientsKept(logger, string.Join(", ", unlisted.Select(entry => entry.ClientId)));
            return;
        }

        // The EF store deletes the client's authorizations and tokens with it.
        foreach (var (application, clientId) in unlisted)
        {
            await applications.DeleteAsync(application, cancellationToken);
            OidcLog.ClientDeleted(logger, clientId);
        }
    }

    // Whether the stored client already is what the options describe, so that a start writes nothing. A secret is stored
    // salted and hashed, so a confidential client is checked against it, which costs one hash. The authorize and logout
    // endpoints compare a redirect URI with the stored one as strings, so the URIs are compared as they are written: one
    // changed only in the case of its host is written as configured, so that a client that sends it is not refused, and
    // UpdateAsync, which compares them as Uri values, ends no consent for it (#3442).
    private static async Task<bool> IsCurrentAsync(IOpenIddictApplicationManager applications, object existing, OpenIddictApplicationDescriptor wanted, CancellationToken cancellationToken)
    {
        var stored = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(stored, existing, cancellationToken);

        var same = stored.ClientType == wanted.ClientType
            && stored.ConsentType == wanted.ConsentType
            && stored.DisplayName == wanted.DisplayName
            && stored.Permissions.SetEquals(wanted.Permissions)
            && stored.Requirements.SetEquals(wanted.Requirements)
            && Written(stored.RedirectUris).SetEquals(Written(wanted.RedirectUris))
            && Written(stored.PostLogoutRedirectUris).SetEquals(Written(wanted.PostLogoutRedirectUris))
            && stored.Properties.Count == wanted.Properties.Count
            && wanted.Properties.All(property => stored.Properties.TryGetValue(property.Key, out var value) && value.GetRawText() == property.Value.GetRawText());
        if (!same)
        {
            return false;
        }

        return wanted.ClientSecret is null
            ? stored.ClientSecret is null
            : await applications.ValidateClientSecretAsync(existing, wanted.ClientSecret, cancellationToken);
    }

    private static HashSet<string> Written(IEnumerable<Uri> uris) => uris.Select(uri => uri.OriginalString).ToHashSet(StringComparer.Ordinal);

    internal static OpenIddictApplicationDescriptor Describe(string clientId, ClientRegistration client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = client.IsConfidential ? client.Secret : null,
            ClientType = client.IsConfidential ? ClientTypes.Confidential : ClientTypes.Public,
            ConsentType = client.RequireConsent ? ConsentTypes.Explicit : ConsentTypes.Implicit,
            DisplayName = client.DisplayName ?? clientId,
        };

        descriptor.Properties[AuthorizationServer.FromOptionsProperty] = JsonSerializer.SerializeToElement(true);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);

        if (client.RedirectUris.Count > 0)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
            descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
            descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

            foreach (var uri in client.RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
            }

            foreach (var uri in client.PostLogoutRedirectUris)
            {
                descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
            }
        }

        // A client of the member pool is never a service account. Every such client may send the direct login grant, so
        // that the token endpoint answers each refusal of it alike; the flag, and the option of the pool, decide there.
        if (client.Pool == ClientPool.Members)
        {
            descriptor.Properties[AuthorizationServer.MembersProperty] = JsonSerializer.SerializeToElement(true);
            descriptor.Permissions.Add(Permissions.Prefixes.GrantType + AuthorizationServer.MemberPasswordGrant);
            if (client.AllowDirectLogin)
            {
                descriptor.Properties[AuthorizationServer.DirectLoginProperty] = JsonSerializer.SerializeToElement(true);
                descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            }
        }
        else if (client.IsConfidential)
        {
            descriptor.Permissions.Add(Permissions.Endpoints.Introspection);
            descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        }

        foreach (var scope in client.Scopes.Where(scope => scope != Scopes.OpenId).Distinct(StringComparer.Ordinal))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        if (client.Roles.Count > 0)
        {
            descriptor.Properties[AuthorizationServer.RolesProperty] = JsonSerializer.SerializeToElement(client.Roles.Distinct(StringComparer.Ordinal).ToArray());
        }

        return descriptor;
    }
}
