using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// Writes the clients of cms.Clients to cms_oidc_application when the host starts, after the migrations. A listed client is
// replaced by what the options say, its secret included. A client the options registered and no longer list is deleted with
// its sessions and tokens, so that dropping a compromised client from the configuration ends it; a client registered some
// other way is left alone.
internal sealed class ClientRegistrationService(IServiceScopeFactory scopes, ClientOptions clients) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        await RemoveUnlistedAsync(applications, cancellationToken);

        foreach (var (clientId, client) in clients.Applications)
        {
            if (await applications.FindByClientIdAsync(clientId, cancellationToken) is { } existing)
            {
                await applications.UpdateAsync(existing, Describe(clientId, client), cancellationToken);
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

                await applications.UpdateAsync(registered, Describe(clientId, client), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RemoveUnlistedAsync(IOpenIddictApplicationManager applications, CancellationToken cancellationToken)
    {
        List<object> unlisted = [];
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            var properties = await applications.GetPropertiesAsync(application, cancellationToken);
            if (properties.ContainsKey(AuthorizationServer.FromOptionsProperty)
                && await applications.GetClientIdAsync(application, cancellationToken) is { } clientId
                && !clients.Applications.ContainsKey(clientId))
            {
                unlisted.Add(application);
            }
        }

        // The EF store deletes the client's authorizations and tokens with it.
        foreach (var application in unlisted)
        {
            await applications.DeleteAsync(application, cancellationToken);
        }
    }

    internal static OpenIddictApplicationDescriptor Describe(string clientId, ClientRegistration client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = client.IsConfidential ? client.Secret : null,
            ClientType = client.IsConfidential ? ClientTypes.Confidential : ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
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

        if (client.IsConfidential)
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
