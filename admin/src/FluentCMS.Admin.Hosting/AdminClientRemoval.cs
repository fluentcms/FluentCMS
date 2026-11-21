using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace FluentCMS.Admin.Hosting;

// While the admin is off, deletes the client that AdminClientRegistration wrote while it was on, with its sessions and tokens.
// The host deletes a client of the options that they no longer list, but keeps them all when the options list none, in case
// the configuration failed to load; the admin's client goes then too, since turning the admin on writes it again whole and
// costs its users a sign-in only. A client of that id that the options list, or that is not the one the registration writes,
// such as one an operator configured under another display name, is left as it is. After the migrations, which run in the
// Starting phase.
internal sealed class AdminClientRemoval(
    AdminOptions admin,
    ClientOptions clients,
    IServiceScopeFactory scopes,
    ILogger<AdminClientRemoval> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (admin.Enabled || clients.Applications.ContainsKey(clients.AdminClientId))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applications.FindByClientIdAsync(clients.AdminClientId, cancellationToken) is not { } application
            || await applications.GetDisplayNameAsync(application, cancellationToken) != AdminClientRegistration.DisplayName
            || !(await applications.GetPropertiesAsync(application, cancellationToken)).ContainsKey(AuthorizationServer.FromOptionsProperty))
        {
            return;
        }

        // The EF store deletes the client's authorizations and tokens with it.
        try
        {
            await applications.DeleteAsync(application, cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            // Another instance that started at the same time deleted it first.
            return;
        }

        OidcLog.ClientDeleted(logger, clients.AdminClientId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
