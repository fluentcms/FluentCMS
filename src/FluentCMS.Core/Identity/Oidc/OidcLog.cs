using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.Oidc;

// 90xx, the event ids of the identity family (FCMS30xx); the authorization server has 9001 to 9003, 9005, 9006, 9008, 9009
// and 9013, and its keys 9014 to 9023. A key is named by its id, use and dates, never by its material or protected_key.
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
    public const int KeyMadeId = 9014;
    public const int KeyActiveId = 9015;
    public const int KeyRetiredId = 9016;
    public const int KeyRemovedId = 9017;
    public const int KeyUnreadableId = 9018;
    public const int KeysRevokedId = 9019;
    public const int KeysNotReadId = 9020;
    public const int KeysNotAppliedId = 9021;
    public const int KeysScheduleFailedId = 9022;
    public const int KeyRetirementSetId = 9023;

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

    [LoggerMessage(EventId = KeyMadeId, EventName = "OidcKeyMade", Level = LogLevel.Information, Message = "Made the key {KeyId} of the authorization server (use {Use}, sequence {Sequence}), which is active from {ActiveFrom:O}.")]
    public static partial void KeyMade(ILogger logger, Guid keyId, string use, int sequence, DateTimeOffset activeFrom);

    [LoggerMessage(EventId = KeyActiveId, EventName = "OidcKeyActive", Level = LogLevel.Information, Message = "The key {KeyId} of the authorization server (use {Use}, active from {ActiveFrom:O}) now signs or encrypts what this instance issues.")]
    public static partial void KeyActive(ILogger logger, Guid keyId, string use, DateTimeOffset activeFrom);

    [LoggerMessage(EventId = KeyRetiredId, EventName = "OidcKeyRetired", Level = LogLevel.Information, Message = "The key {KeyId} of the authorization server (use {Use}) retired at {RetiredAt:O}; this instance no longer accepts what it signed or encrypted.")]
    public static partial void KeyRetired(ILogger logger, Guid keyId, string use, DateTimeOffset retiredAt);

    [LoggerMessage(EventId = KeyRemovedId, EventName = "OidcKeyRemoved", Level = LogLevel.Information, Message = "Removed the key {KeyId} of the authorization server (use {Use}, created at {CreatedAt:O}), which retired at {RetiredAt:O}.")]
    public static partial void KeyRemoved(ILogger logger, Guid keyId, string use, DateTimeOffset createdAt, DateTimeOffset retiredAt);

    [LoggerMessage(EventId = KeyUnreadableId, EventName = "OidcKeyUnreadable", Level = LogLevel.Error, Message = "The Data Protection key ring cannot read the key {KeyId} of the authorization server (use {Use}, active from {ActiveFrom:O}), so it is left out and what it signed or encrypted is refused. When it is the active key the server issues nothing: restore the ring key or certificate that protected it, or run 'fluentcms keys revoke --force'.")]
    public static partial void KeyUnreadable(ILogger logger, Guid keyId, string use, DateTimeOffset activeFrom);

    [LoggerMessage(EventId = KeysRevokedId, EventName = "OidcKeysRevoked", Level = LogLevel.Warning, Message = "Revoked the keys of the authorization server: retired {Keys} keys (uses {Uses}) at {RetiredAt:O}, and revoked {Tokens} tokens and {Authorizations} authorizations.")]
    public static partial void KeysRevoked(ILogger logger, int keys, string uses, DateTimeOffset retiredAt, long tokens, long authorizations);

    [LoggerMessage(EventId = KeysNotReadId, EventName = "OidcKeysNotRead", Level = LogLevel.Error, Message = "Could not read the keys of the authorization server; this instance keeps the keys it has, moves them between their states by its clock, and reads again in {RetrySeconds} seconds.")]
    public static partial void KeysNotRead(ILogger logger, Exception exception, int retrySeconds);

    [LoggerMessage(EventId = KeysNotAppliedId, EventName = "OidcKeysNotApplied", Level = LogLevel.Error, Message = "Could not build the options of the authorization server from its keys; they are built again on the next request.")]
    public static partial void KeysNotApplied(ILogger logger, Exception exception);

    [LoggerMessage(EventId = KeysScheduleFailedId, EventName = "OidcKeysScheduleFailed", Level = LogLevel.Error, Message = "Could not register the recurring job that rotates and removes the keys of the authorization server; retired keys stay, and none is rotated, until it is registered.")]
    public static partial void KeysScheduleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = KeyRetirementSetId, EventName = "OidcKeyRetirementSet", Level = LogLevel.Information, Message = "The key {KeyId} of the authorization server (use {Use}) is accepted until {RetiredAt:O}.")]
    public static partial void KeyRetirementSet(ILogger logger, Guid keyId, string use, DateTimeOffset retiredAt);
}
