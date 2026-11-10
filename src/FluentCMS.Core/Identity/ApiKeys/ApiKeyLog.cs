using System.Net;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.ApiKeys;

// 90xx, the event ids of the identity family (FCMS30xx); API keys have 9010.
internal static partial class ApiKeyLog
{
    public const int RefusedId = 9010;

    [LoggerMessage(EventId = RefusedId, EventName = "ApiKeyRefused", Level = LogLevel.Information, Message = "{Code}: refused the API key of a request from {RemoteIp}; the key found by it, if any, is {KeyId}.")]
    public static partial void Refused(ILogger logger, string code, IPAddress? remoteIp, Guid? keyId);

    public const int RefusalNotRecordedId = 9011;

    [LoggerMessage(EventId = RefusalNotRecordedId, EventName = "ApiKeyRefusalNotRecorded", Level = LogLevel.Warning, Message = "The activity row of the refused address of API key {KeyId} could not be written.")]
    public static partial void RefusalNotRecorded(ILogger logger, Exception exception, Guid keyId);
}
