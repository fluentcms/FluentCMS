using System.Security.Cryptography;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// What a client asked for in its registration, once DynamicClientRegistration has checked it.
internal sealed record ClientMetadata(string Name, IReadOnlyList<Uri> RedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes);

// The client a registration made, or nothing when too many clients wait for approval.
internal sealed record RegisteredClient(string ClientId, DateTimeOffset IssuedAt, McpClientStatus Status);

// The MCP clients that registered themselves (RFC 7591), over OpenIddict's application manager: the SDK's IMcpClients for
// the MCP plugin, and the registration itself for the endpoint. Such a client is public, uses the code flow with PKCE and
// refresh tokens, asks the user's consent, and may ask for the scopes of AuthorizationServer.RegisteredClientScopes only.
// Its state is one property of its application (McpClientState). A change and its event commit together, and nothing here
// checks a permission: the plugin's service does.
internal sealed class McpClients(
    IOpenIddictApplicationManager applications,
    SystemDbContext context,
    IdentityChanges changes,
    IEventBus events,
    IOperationContextAccessor accessor,
    TimeProvider time) : IMcpClients
{
    public async Task<IReadOnlyList<McpClient>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<McpClient> clients = [];
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (await DescribeAsync(application, cancellationToken) is { } client)
            {
                clients.Add(client);
            }
        }

        var roleIds = clients.Select(client => client.RoleId).OfType<Guid>().Distinct().ToList();
        var roles = await context.Set<Role>().AsNoTracking()
            .Where(role => roleIds.Contains(role.Id))
            .ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);

        // The newest token of each client is its last activity: a sign-in or a refresh.
        var ids = clients.Select(client => client.Id).ToList();
        var newest = await context.Set<OpenIddictEntityFrameworkCoreToken<Guid>>().AsNoTracking()
            .Where(token => token.Application != null && ids.Contains(token.Application.Id))
            .GroupBy(token => token.Application!.Id)
            .Select(group => new { Id = group.Key, At = group.Max(token => token.CreationDate) })
            .ToDictionaryAsync(row => row.Id, row => row.At, cancellationToken);

        return
        [
            .. clients
                .Select(client => client with
                {
                    RoleName = client.RoleId is { } role ? roles.GetValueOrDefault(role) : null,
                    LastActivityAt = newest.GetValueOrDefault(client.Id) is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null,
                })
                .OrderByDescending(client => client.RegisteredAt)
                .ThenBy(client => client.ClientId, StringComparer.Ordinal),
        ];
    }

    public async Task<McpClientChange> ApproveAsync(Guid id, string role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        try
        {
            return await changes.RunAsync(
                async () =>
                {
                    if (await FindAsync(id, cancellationToken) is not var (application, client, state))
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    if (state.Status != McpClientStatus.Pending)
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotPending, client);
                    }

                    if (await FindRoleAsync(role, cancellationToken) is not { } found)
                    {
                        return new McpClientChange(McpClientChangeOutcome.UnknownRole, client);
                    }

                    var approved = state with
                    {
                        Status = McpClientStatus.Approved,
                        RoleId = found.Id,
                        ApprovedBy = accessor.Current?.AuditPrincipal.Id,
                        ApprovedAt = time.GetUtcNow(),
                    };
                    await StoreAsync(application, approved, cancellationToken);
                    await changes.RaiseAsync(new McpClientApprovedEvent(client.Id, client.ClientId, client.Name, found.Id, found.Name), cancellationToken);
                    return new McpClientChange(McpClientChangeOutcome.Done, With(client, approved) with { RoleName = found.Name });
                },
                cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return await ChangedMeanwhileAsync(id, cancellationToken);
        }
    }

    public async Task<McpClientChange> RejectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await changes.RunAsync(
                async () =>
                {
                    if (await FindAsync(id, cancellationToken) is not var (application, client, state))
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                    }

                    if (state.Status != McpClientStatus.Pending)
                    {
                        return new McpClientChange(McpClientChangeOutcome.NotPending, client);
                    }

                    var rejected = state with { Status = McpClientStatus.Rejected };
                    await StoreAsync(application, rejected, cancellationToken);
                    await changes.RaiseAsync(new McpClientRejectedEvent(client.Id, client.ClientId, client.Name), cancellationToken);
                    return new McpClientChange(McpClientChangeOutcome.Done, With(client, rejected));
                },
                cancellationToken);
        }
        catch (OpenIddictExceptions.ConcurrencyException)
        {
            return await ChangedMeanwhileAsync(id, cancellationToken);
        }
    }

    // The EF store deletes the client's authorizations and tokens with it, and a bearer request reads its token's row, so
    // an access token of the client is refused from the next request on.
    public Task<McpClientChange> RevokeAsync(Guid id, CancellationToken cancellationToken = default) =>
        changes.RunAsync(
            async () =>
            {
                if (await FindAsync(id, cancellationToken) is not var (application, client, _))
                {
                    return new McpClientChange(McpClientChangeOutcome.NotFound, null);
                }

                await applications.DeleteAsync(application, cancellationToken);
                await changes.RaiseAsync(new McpClientRevokedEvent(client.Id, client.ClientId, client.Name), cancellationToken);
                return new McpClientChange(McpClientChangeOutcome.Done, client);
            },
            cancellationToken);

    public async Task<int> RemoveUnapprovedAsync(DateTimeOffset registeredBefore, CancellationToken cancellationToken = default)
    {
        List<object> stale = [];
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is { IsApproved: false } state
                && state.RegisteredAt < registeredBefore)
            {
                stale.Add(application);
            }
        }

        foreach (var application in stale)
        {
            await applications.DeleteAsync(application, cancellationToken);
        }

        return stale.Count;
    }

    // Registers a client: approved at once with the default role under the open policy when that role exists, and pending
    // otherwise. Null when as many clients wait for approval as the policy allows. The count comes before the insert, so
    // registrations that cross may pass the cap by the few in flight.
    public async Task<RegisteredClient?> RegisterAsync(ClientMetadata metadata, McpClientRegistrationPolicy policy, EventSource source, string? ip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(source);

        var role = policy.Mode == McpClientRegistrationMode.Open && policy.DefaultRole is { Length: > 0 } named
            ? await FindRoleAsync(named, cancellationToken)
            : null;
        if (role is null && await CountPendingAsync(cancellationToken) >= policy.MaxPendingClients)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var state = role is null
            ? new McpClientState(McpClientStatus.Pending, now)
            : new McpClientState(McpClientStatus.Approved, now) { RoleId = role.Id, ApprovedAt = now };

        var clientId = "mcp-" + RandomNumberGenerator.GetHexString(32, lowercase: true);
        var descriptor = Describe(clientId, metadata);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = state.ToProperty();

        return await changes.RunAsync(
            async () =>
            {
                var application = await applications.CreateAsync(descriptor, cancellationToken);
                var id = Guid.Parse(await applications.GetIdAsync(application, cancellationToken) ?? "");
                var registered = new McpClientRegisteredEvent(id, clientId, metadata.Name, StatusName(state.Status), state.RoleId, ip)
                {
                    Source = source with { ClientId = clientId },
                };
                await events.PublishAsync(registered, cancellationToken);
                return new RegisteredClient(clientId, now, state.Status);
            },
            cancellationToken);
    }

    public static string StatusName(McpClientStatus status) => status switch
    {
        McpClientStatus.Approved => "approved",
        McpClientStatus.Rejected => "rejected",
        _ => "pending",
    };

    internal static OpenIddictApplicationDescriptor Describe(string clientId, ClientMetadata metadata)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = metadata.Name,
        };

        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.Revocation);
        descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        foreach (var grant in metadata.GrantTypes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.GrantType + grant);
        }

        // openid needs no permission; any other scope does, so a scope left out here is refused at the authorize endpoint.
        foreach (var scope in metadata.Scopes.Where(scope => scope != Scopes.OpenId && AuthorizationServer.RegisteredClientScopes.Contains(scope, StringComparer.Ordinal)))
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        foreach (var uri in metadata.RedirectUris)
        {
            descriptor.RedirectUris.Add(uri);
        }

        return descriptor;
    }

    private async Task<int> CountPendingAsync(CancellationToken cancellationToken)
    {
        var pending = 0;
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            if (McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is { Status: McpClientStatus.Pending })
            {
                pending++;
            }
        }

        return pending;
    }

    // Another change got there first, so the client is no longer what this one read.
    private async Task<McpClientChange> ChangedMeanwhileAsync(Guid id, CancellationToken cancellationToken) =>
        await FindAsync(id, cancellationToken) is var (_, client, _)
            ? new McpClientChange(McpClientChangeOutcome.NotPending, client)
            : new McpClientChange(McpClientChangeOutcome.NotFound, null);

    private async Task<(object Application, McpClient Client, McpClientState State)?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await applications.FindByIdAsync(id.ToString("D"), cancellationToken) is not { } application
            || McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is not { } state
            || await DescribeAsync(application, cancellationToken) is not { } client)
        {
            return null;
        }

        return (application, client, state);
    }

    // Null for a client that did not register itself, which this service neither lists nor changes.
    private async Task<McpClient?> DescribeAsync(object application, CancellationToken cancellationToken)
    {
        if (McpClientState.Of(await applications.GetPropertiesAsync(application, cancellationToken)) is not { } state)
        {
            return null;
        }

        var clientId = await applications.GetClientIdAsync(application, cancellationToken) ?? "";
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        var client = new McpClient(
            Guid.Parse(await applications.GetIdAsync(application, cancellationToken) ?? ""),
            clientId,
            await applications.GetDisplayNameAsync(application, cancellationToken) ?? clientId,
            state.Status,
            [.. await applications.GetRedirectUrisAsync(application, cancellationToken)],
            state.RegisteredAt)
        {
            Scopes =
            [
                Scopes.OpenId,
                .. permissions
                    .Where(permission => permission.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
                    .Select(permission => permission[Permissions.Prefixes.Scope.Length..])
                    .Order(StringComparer.Ordinal),
            ],
        };
        return With(client, state);
    }

    private static McpClient With(McpClient client, McpClientState state) => client with
    {
        Status = state.Status,
        RoleId = state.RoleId,
        ApprovedBy = state.ApprovedBy,
        ApprovedAt = state.ApprovedAt,
    };

    private async Task StoreAsync(object application, McpClientState state, CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application, cancellationToken);
        descriptor.Properties[AuthorizationServer.McpClientProperty] = state.ToProperty();
        await applications.UpdateAsync(application, descriptor, cancellationToken);
    }

    // A role by its id, or an instance-wide role by its exact name, since a name is unique only within its site.
    private async Task<Role?> FindRoleAsync(string reference, CancellationToken cancellationToken)
    {
        var trimmed = reference.Trim();
        return Guid.TryParse(trimmed, out var id)
            ? await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(role => role.Id == id, cancellationToken)
            : await context.Set<Role>().AsNoTracking().FirstOrDefaultAsync(role => role.SiteId == null && role.Name == trimmed, cancellationToken);
    }
}
