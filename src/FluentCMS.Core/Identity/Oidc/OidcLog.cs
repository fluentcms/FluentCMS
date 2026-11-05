using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Oidc;

// Event ids in the identity range of the FCMS codes, after the codes themselves.
internal static partial class OidcLog
{
    public const int PruneScheduleFailedId = 3030;
    public const int ClientDeletedId = 3031;
    public const int ClientsKeptId = 3032;

    [LoggerMessage(EventId = PruneScheduleFailedId, EventName = "OidcPruneScheduleFailed", Level = LogLevel.Error, Message = "Could not register the recurring prune job of the authorization server; used tokens stay until it is registered.")]
    public static partial void PruneScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = ClientDeletedId, EventName = "OidcClientDeleted", Level = LogLevel.Warning, Message = "Deleted the OAuth client {ClientId}, with its sessions and tokens, because cms.Clients no longer lists it.")]
    public static partial void ClientDeleted(ILogger logger, string clientId);

    [LoggerMessage(EventId = ClientsKeptId, EventName = "OidcClientsKept", Level = LogLevel.Warning, Message = "cms.Clients lists no client, so the clients it registered before are kept: {ClientIds}. A configuration that failed to load would otherwise sign everyone out.")]
    public static partial void ClientsKept(ILogger logger, string clientIds);
}
