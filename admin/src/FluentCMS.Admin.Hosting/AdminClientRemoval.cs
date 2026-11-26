using FluentCMS.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace FluentCMS.Admin.Hosting;

// While the admin is off, deletes the client that AdminClientRegistration wrote while it was on, with its sessions and tokens.
// The host deletes a client of the options that they no longer list, but keeps them all when the options list none, in case
// the configuration failed to load; the admin's client goes then too, since turning the admin on writes it again whole and
// costs its users a sign-in only. While the options list any client this leaves the row to the host, so that the two never
// delete the same one. An unlisted client of the options under that id with the registration's display name is taken for its
// own; one registered some other way, or under another display name, is left as it is. After the migrations, which run in
// the Starting phase.
internal sealed partial class AdminClientRemoval(
    AdminOptions admin,
    ClientOptions clients,
    IServiceScopeFactory scopes,
    ILogger<AdminClientRemoval> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (admin.Enabled || clients.Applications.Count > 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        if (await applications.FindByClientIdAsync(clients.AdminClientId, cancellationToken) is not { } application
            || await applications.GetDisplayNameAsync(application, cancellationToken) != AdminClientRegistration.DisplayName
            || !(await applications.GetPropertiesAsync(application, cancellationToken)).ContainsKey(ClientOptions.FromOptionsProperty))
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
            // Changed or gone since it was read: deleted by another instance, or updated by one with the admin on. It is left
            // for the next start.
            return;
        }
        catch (DbUpdateException exception)
        {
            // Any failure of the save, where Core's removal of MCP clients takes only the race of a grant (a foreign key's
            // refusal, or SQL Server's deadlock) and throws the rest: a consent or a token that arrived during the delete, as
            // from an instance with the admin still on in a rolling deploy, or a database that cannot be written. A host with
            // the admin off runs as well with the client left, so its start does not fail over it; the row and its tokens stay,
            // with the warning below, until a start whose delete succeeds.
            DeleteRefused(logger, exception, clients.AdminClientId);
            return;
        }

        ClientDeleted(logger, clients.AdminClientId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The event of the host's own deletion of a client the options no longer list, so that an operator finds both alike.
    [LoggerMessage(EventId = 9002, EventName = "OidcClientDeleted", Level = LogLevel.Warning, Message = "Deleted the OAuth client {ClientId}, with its sessions and tokens, because cms.Clients no longer lists it.")]
    private static partial void ClientDeleted(ILogger logger, string clientId);

    [LoggerMessage(EventId = 40, EventName = "AdminClientDeleteRefused", Level = LogLevel.Warning, Message = "Could not delete the OAuth client {ClientId} of the admin, which is off: the database refused the delete, as it does when a consent or a token arrives while the client is deleted. The client is left for the next start.")]
    private static partial void DeleteRefused(ILogger logger, Exception exception, string clientId);
}
