using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Plugins;

// Event ids 9230 to 9239, per-site enablement.
internal static partial class PluginLog
{
    [LoggerMessage(EventId = 9230, Level = LogLevel.Information, Message = "The plugin {Plugin} was enabled for the site {SiteId}.")]
    public static partial void Enabled(ILogger logger, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9231, Level = LogLevel.Information, Message = "The plugin {Plugin} was disabled for the site {SiteId}.")]
    public static partial void Disabled(ILogger logger, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9232, Level = LogLevel.Information, Message = "Job {JobId} ({Type}) completed without running, since the plugin {Plugin} is disabled for the site {SiteId}.")]
    public static partial void JobSkipped(ILogger logger, Guid jobId, string type, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9233, Level = LogLevel.Information, Message = "Outbox delivery {Id} to {Handler} waits, since the plugin {Plugin} is disabled for the site {SiteId}; it is delivered once the plugin is enabled there again.")]
    public static partial void DeliveryWaits(ILogger logger, Guid id, string handler, string plugin, Guid siteId);
}
