using FluentCMS.Agents;
using FluentCMS.Authorization;

namespace FluentCMS.Plugins.Mcp;

public sealed record McpClientApproval(string? Role);

public sealed record McpClientInfo(
    Guid Id,
    string ClientId,
    string Name,
    string Status,
    Guid? RoleId,
    string? RoleName,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> Scopes,
    DateTimeOffset RegisteredAt,
    Guid? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? LastActivityAt);

// The MCP clients that registered themselves, for the Management API. A client belongs to the instance, as the
// authorization server does, so every call needs mcp.manage from a grant with no site limit. Core keeps the clients and
// makes the changes behind IMcpClients, which checks nothing; this service decides who may ask.
internal sealed class McpClientService(IMcpClients clients, IPermissionChecker permissions)
{
    public async Task<IReadOnlyList<McpClientInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return [.. (await clients.ListAsync(cancellationToken)).Select(Info)];
    }

    public async Task<McpClientInfo> ApproveAsync(Guid id, McpClientApproval? approval, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        if (approval?.Role is not { } role || string.IsNullOrWhiteSpace(role))
        {
            throw new PluginException(McpErrorCodes.ClientRoleRequired, "Approving a client names its role: the id of a role, or the name of an instance-wide role.");
        }

        return Changed(id, await clients.ApproveAsync(id, role, cancellationToken), role);
    }

    public async Task<McpClientInfo> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return Changed(id, await clients.RejectAsync(id, cancellationToken), role: null);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        Changed(id, await clients.RevokeAsync(id, cancellationToken), role: null);
    }

    private ValueTask RequireAsync(CancellationToken cancellationToken) => permissions.RequireAsync(McpPermissions.Manage, site: null, cancellationToken);

    private static McpClientInfo Changed(Guid id, McpClientChange change, string? role) => change.Outcome switch
    {
        McpClientChangeOutcome.Done => Info(change.Client!),
        McpClientChangeOutcome.NotPending => throw new PluginException(McpErrorCodes.ClientNotPending, $"The MCP client {id:D} is {StatusOf(change.Client!.Status)}; only a client that waits for approval is approved or rejected."),
        McpClientChangeOutcome.UnknownRole => throw new PluginException(McpErrorCodes.ClientRoleUnknown, $"There is no role '{role}'. A role is named by its id, or by its name when it is instance-wide."),
        _ => throw new PluginException(McpErrorCodes.ClientNotFound, $"There is no MCP client {id:D}."),
    };

    private static McpClientInfo Info(McpClient client) => new(
        client.Id,
        client.ClientId,
        client.Name,
        StatusOf(client.Status),
        client.RoleId,
        client.RoleName,
        client.RedirectUris,
        client.Scopes,
        client.RegisteredAt,
        client.ApprovedBy,
        client.ApprovedAt,
        client.LastActivityAt);

    private static string StatusOf(McpClientStatus status) => status switch
    {
        McpClientStatus.Approved => "approved",
        McpClientStatus.Rejected => "rejected",
        _ => "pending",
    };
}
