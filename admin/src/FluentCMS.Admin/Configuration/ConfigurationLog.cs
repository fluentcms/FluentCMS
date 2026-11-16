using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Configuration;

internal static partial class ConfigurationLog
{
    [LoggerMessage(EventId = 1, EventName = "UnknownConfigurationMember", Level = LogLevel.Warning, Message = "config.json has the member {Member}, which this admin does not know; it is ignored.")]
    public static partial void UnknownMember(ILogger logger, string member);

    [LoggerMessage(EventId = 2, EventName = "PlainHttpConfigurationMember", Level = LogLevel.Warning, Message = "{Member} of config.json is plain http on a host that is not loopback; tokens and content travel unencrypted. Use https.")]
    public static partial void PlainHttp(ILogger logger, string member);
}
