using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Oidc;

// 90xx, the event ids of the identity family (FCMS30xx); the authorization server has 9001 to 9003 and 9005.
internal static partial class OidcLog
{
    public const int PruneScheduleFailedId = 9001;
    public const int ClientDeletedId = 9002;
    public const int ClientsKeptId = 9003;
    public const int ClientRegistrationCappedId = 9005;

    [LoggerMessage(EventId = PruneScheduleFailedId, EventName = "OidcPruneScheduleFailed", Level = LogLevel.Error, Message = "Could not register the recurring prune job of the authorization server; used tokens stay until it is registered.")]
    public static partial void PruneScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = ClientDeletedId, EventName = "OidcClientDeleted", Level = LogLevel.Warning, Message = "Deleted the OAuth client {ClientId}, with its sessions and tokens, because cms.Clients no longer lists it.")]
    public static partial void ClientDeleted(ILogger logger, string clientId);

    [LoggerMessage(EventId = ClientsKeptId, EventName = "OidcClientsKept", Level = LogLevel.Warning, Message = "cms.Clients lists no client, so the clients it registered before are kept: {ClientIds}. A configuration that failed to load would otherwise sign everyone out.")]
    public static partial void ClientsKept(ILogger logger, string clientIds);

    [LoggerMessage(EventId = ClientRegistrationCappedId, EventName = "OidcClientRegistrationCapped", Level = LogLevel.Warning, Message = "Refused the registration of an MCP client: {MaxPendingClients} clients wait for approval already. Approve, reject or delete them.")]
    public static partial void ClientRegistrationCapped(ILogger logger, int maxPendingClients);
}
