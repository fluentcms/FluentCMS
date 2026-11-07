using System.Net;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Identity.ApiKeys;

// 90xx, the event ids of the identity family (FCMS30xx); API keys have 9010.
internal static partial class ApiKeyLog
{
    public const int RefusedId = 9010;

    [LoggerMessage(EventId = RefusedId, EventName = "ApiKeyRefused", Level = LogLevel.Information, Message = "{Code}: refused the API key of a request from {RemoteIp}; the key found by it, if any, is {KeyId}.")]
    public static partial void Refused(ILogger logger, string code, IPAddress? remoteIp, Guid? keyId);
}
