using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;

namespace FluentCMS.Plugins.Mcp;

public sealed record McpClientApproval(string? Role);

public sealed record McpClientRegistrationRequest(string? Name, IReadOnlyList<string>? RedirectUris, string? Role);

public sealed record McpClientRoleChange(string? Role);

public sealed record McpClientRemoval(string? RegisteredFrom, string? RegisteredBefore);

public sealed record McpClientRemovalResult(int Removed, bool More);

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
    DateTimeOffset? LastActivityAt,
    DateTimeOffset? UsedAt,
    Guid? UsedBy);

// A client the open policy approved that counts toward the caller's maxConsentsPerUser.
public sealed record McpConsentInfo(
    Guid Id,
    string ClientId,
    string Name,
    IReadOnlyList<string> RedirectUris,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? ConsentedAt,
    bool Active);

// The MCP clients that registered themselves, and those an administrator registers, for the Management API. A client
// belongs to the instance, as the authorization server does, so every call needs mcp.manage from a grant with no site
// limit, but those of the caller's own consents. Core keeps the clients and makes the changes behind IMcpClients, which
// checks nothing; this service decides who may ask. A role is checked the same way wherever it is given: it must exist,
// and the caller need not hold it, since a client's role only narrows what its users' own roles allow.
internal sealed class McpClientService(IMcpClients clients, IPermissionChecker permissions, IOperationContextAccessor operations)
{
    // The most clients one bulk removal removes.
    public const int MaxRemovedPerCall = 100;

    public async Task<IReadOnlyList<McpClientInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        return [.. (await clients.ListAsync(cancellationToken)).Select(Info)];
    }

    public async Task<McpClientInfo> RegisterAsync(McpClientRegistrationRequest? request, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var role = RoleOf(request?.Role, "Registering a client names its role");
        var registration = new McpClientRegistration(request!.Name, request.RedirectUris ?? [], role);
        return Changed(Guid.Empty, await clients.RegisterAsync(registration, cancellationToken), role);
    }

    public async Task<McpClientInfo> ApproveAsync(Guid id, McpClientApproval? approval, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var role = RoleOf(approval?.Role, "Approving a client names its role");
        return Changed(id, await clients.ApproveAsync(id, role, cancellationToken), role);
    }

    public async Task<McpClientInfo> ChangeRoleAsync(Guid id, McpClientRoleChange? change, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var role = RoleOf(change?.Role, "Changing the role of a client names the role");
        return Changed(id, await clients.ChangeRoleAsync(id, role, cancellationToken), role);
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

    // The clients the open policy approved that registered in the span the request names, removed as RevokeAsync removes
    // one and under the same permission. A request that names no time would remove every such client, so it is refused.
    // A call removes MaxRemovedPerCall at most, each in a transaction of its own, and says when it stopped there.
    public async Task<McpClientRemovalResult> RemoveOpenApprovedAsync(McpClientRemoval? removal, CancellationToken cancellationToken)
    {
        await RequireAsync(cancellationToken);
        var from = Time(removal?.RegisteredFrom, "registeredFrom");
        var before = Time(removal?.RegisteredBefore, "registeredBefore");
        if (from is null && before is null)
        {
            throw new PluginException(McpErrorCodes.ClientRemovalInvalid, "A removal names the span of time its clients registered in: 'registeredFrom', 'registeredBefore' or both.");
        }

        if (from >= before)
        {
            throw new PluginException(McpErrorCodes.ClientRemovalInvalid, $"The span holds no time: 'registeredFrom' ({from:O}) must be earlier than 'registeredBefore' ({before:O}).");
        }

        var removed = await clients.RemoveOpenApprovedAsync(from, before, MaxRemovedPerCall, cancellationToken);
        return new McpClientRemovalResult(removed, removed >= MaxRemovedPerCall);
    }

    // A user's own consents need no permission, and are only ever the caller's: a caller that is no user, such as an API
    // key, holds none, and a client that counts for another user is not found.
    public async Task<IReadOnlyList<McpConsentInfo>> ListConsentsAsync(CancellationToken cancellationToken) =>
        CallingUser() is { } user ? [.. (await clients.ListConsentsAsync(user, cancellationToken)).Select(ConsentInfo)] : [];

    public async Task EndConsentAsync(Guid id, CancellationToken cancellationToken)
    {
        var change = CallingUser() is { } user ? await clients.EndConsentAsync(user, id, cancellationToken) : new McpClientChange(McpClientChangeOutcome.NotFound, null);
        switch (change.Outcome)
        {
            case McpClientChangeOutcome.Done:
                return;
            case McpClientChangeOutcome.ChangedMeanwhile:
                throw new PluginException(McpErrorCodes.ClientChangedMeanwhile, $"The MCP client {id:D} or your consent to it was changed by another request at the same time, so its place was not freed; send the request again.");
            default:
                throw new PluginException(McpErrorCodes.ClientNotFound, $"No MCP client {id:D} counts toward your consents.");
        }
    }

    private Guid? CallingUser() => operations.Current?.Principal is { Kind: PrincipalKind.AdminUser } principal ? principal.Id : null;

    // Read as the schedule route reads its time: one written without an offset would be taken in the zone of the server,
    // and the call would remove the clients of another span.
    private static DateTimeOffset? Time(string? text, string name)
    {
        if (text is null)
        {
            return null;
        }

        var value = JsonSerializer.SerializeToElement(text);
        return value.TryGetDateTime(out var parsed) && parsed.Kind != DateTimeKind.Unspecified && value.TryGetDateTimeOffset(out var instant)
            ? instant
            : throw new PluginException(McpErrorCodes.ClientRemovalInvalid, $"'{name}' is an ISO 8601 date and time with an offset, such as 2027-03-01T09:00:00Z.");
    }

    private ValueTask RequireAsync(CancellationToken cancellationToken) => permissions.RequireAsync(McpPermissions.Manage, site: null, cancellationToken);

    private static string RoleOf(string? role, string asking) =>
        string.IsNullOrWhiteSpace(role)
            ? throw new PluginException(McpErrorCodes.ClientRoleRequired, $"{asking}: the id of a role, or the name of an instance-wide role.")
            : role;

    private static McpClientInfo Changed(Guid id, McpClientChange change, string? role) => change.Outcome switch
    {
        McpClientChangeOutcome.Done => Info(change.Client!),
        McpClientChangeOutcome.NotPending => throw new PluginException(McpErrorCodes.ClientNotPending, $"The MCP client {id:D} is {StatusOf(change.Client!.Status)}; only a client that waits for approval is approved or rejected."),
        McpClientChangeOutcome.NotApproved => throw new PluginException(McpErrorCodes.ClientNotApproved, $"The MCP client {id:D} is {StatusOf(change.Client!.Status)}; only an approved client changes its role, and a pending one is approved with a role."),
        McpClientChangeOutcome.UnknownRole => throw new PluginException(McpErrorCodes.ClientRoleUnknown, $"There is no role '{role}'. A role is named by its id, or by its name when it is instance-wide."),
        McpClientChangeOutcome.InvalidRegistration => throw new PluginException(McpErrorCodes.ClientRegistrationInvalid, change.Error ?? "The client cannot be registered."),
        McpClientChangeOutcome.ChangedMeanwhile => throw new PluginException(McpErrorCodes.ClientChangedMeanwhile, $"The MCP client {id:D} was changed by another request at the same time; read it again before changing it."),
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
        client.LastActivityAt,
        client.UsedAt,
        client.UsedBy);

    private static McpConsentInfo ConsentInfo(McpClientConsent consent) => new(
        consent.Client.Id,
        consent.Client.ClientId,
        consent.Client.Name,
        consent.Client.RedirectUris,
        consent.Client.RegisteredAt,
        consent.ConsentedAt,
        consent.Active);

    private static string StatusOf(McpClientStatus status) => status switch
    {
        McpClientStatus.Approved => "approved",
        McpClientStatus.Rejected => "rejected",
        _ => "pending",
    };
}
