using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Oidc;

// 90xx, the event ids of the identity family (FCMS30xx); the authorization server has 9001 to 9003, 9005, 9006, 9008, 9009 and 9013.
internal static partial class OidcLog
{
    public const int PruneScheduleFailedId = 9001;
    public const int ClientDeletedId = 9002;
    public const int ClientsKeptId = 9003;
    public const int ClientRegistrationCappedId = 9005;
    public const int ClientLimitReachedId = 9006;
    public const int ConsentLimitReachedId = 9008;
    public const int ClientDeleteRefusedId = 9009;
    public const int ClientDeleteFailedId = 9013;

    [LoggerMessage(EventId = PruneScheduleFailedId, EventName = "OidcPruneScheduleFailed", Level = LogLevel.Error, Message = "Could not register the recurring prune job of the authorization server; used tokens stay until it is registered.")]
    public static partial void PruneScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = ClientDeletedId, EventName = "OidcClientDeleted", Level = LogLevel.Warning, Message = "Deleted the OAuth client {ClientId}, with its sessions and tokens, because cms.Clients no longer lists it.")]
    public static partial void ClientDeleted(ILogger logger, string clientId);

    [LoggerMessage(EventId = ClientsKeptId, EventName = "OidcClientsKept", Level = LogLevel.Warning, Message = "cms.Clients lists no client, so the clients it registered before are kept: {ClientIds}. A configuration that failed to load would otherwise sign everyone out.")]
    public static partial void ClientsKept(ILogger logger, string clientIds);

    [LoggerMessage(EventId = ClientRegistrationCappedId, EventName = "OidcClientRegistrationCapped", Level = LogLevel.Warning, Message = "Refused the registration of an MCP client: {MaxPendingClients} clients wait for approval already. Approve, reject or delete them.")]
    public static partial void ClientRegistrationCapped(ILogger logger, int maxPendingClients);

    [LoggerMessage(EventId = ClientLimitReachedId, EventName = "OidcClientLimitReached", Level = LogLevel.Warning, Message = "Refused the registration of an MCP client: there are {MaxClients} MCP clients already. Delete the ones not in use, or raise MaxClients.")]
    public static partial void ClientLimitReached(ILogger logger, int maxClients);

    [LoggerMessage(EventId = ConsentLimitReachedId, EventName = "OidcConsentLimitReached", Level = LogLevel.Warning, Message = "Refused the consent of the user {UserId} to the MCP client {ClientId}: the user keeps {MaxConsentsPerUser} clients that the open policy approved already, as GET /api/management/v1/mcp/consents lists them to the user, some of them perhaps with a consent that was ended. The user frees one with DELETE /api/management/v1/mcp/consents/{{id}}; an administrator removes the clients not in use, or raises MaxConsentsPerUser.")]
    public static partial void ConsentLimitReached(ILogger logger, Guid userId, string clientId, int maxConsentsPerUser);

    [LoggerMessage(EventId = ClientDeleteRefusedId, EventName = "OidcClientDeleteRefused", Level = LogLevel.Warning, Message = "Could not delete the MCP client {Id}: the database refused the delete, as it does when a consent or a token arrives while the client is deleted. The client is left for the next run or call.")]
    public static partial void ClientDeleteRefused(ILogger logger, Exception exception, Guid id);

    [LoggerMessage(EventId = ClientDeleteFailedId, EventName = "OidcClientDeleteFailed", Level = LogLevel.Error, Message = "Could not delete the MCP client {Id}. The cleanup went on with the other clients, and its run fails at the end so that the job runner tries again.")]
    public static partial void ClientDeleteFailed(ILogger logger, Exception exception, Guid id);
}
