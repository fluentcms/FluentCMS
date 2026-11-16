using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Configuration;

internal static partial class ConfigurationLog
{
    [LoggerMessage(EventId = 1, EventName = "UnknownConfigurationMember", Level = LogLevel.Warning, Message = "config.json has the member {Member}, which this admin does not know; it is ignored.")]
    public static partial void UnknownMember(ILogger logger, string member);
}
