using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Plugins;

// Event ids 9230 to 9239: per-site enablement, and what the plugin host says of itself.
internal static partial class PluginLog
{
    [LoggerMessage(EventId = 9230, Level = LogLevel.Information, Message = "The plugin {Plugin} was enabled for the site {SiteId}.")]
    public static partial void Enabled(ILogger logger, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9231, Level = LogLevel.Information, Message = "The plugin {Plugin} was disabled for the site {SiteId}.")]
    public static partial void Disabled(ILogger logger, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9232, Level = LogLevel.Information, Message = "Job {JobId} ({Type}) completed without running, since the plugin {Plugin} is disabled for the site {SiteId}.")]
    public static partial void JobSkipped(ILogger logger, Guid jobId, string type, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9233, Level = LogLevel.Debug, Message = "Outbox delivery {Id} to {Handler} waits, since the plugin {Plugin} is disabled for the site {SiteId}; it is delivered once the plugin is enabled there again.")]
    public static partial void DeliveryWaits(ILogger logger, Guid id, string handler, string plugin, Guid siteId);

    [LoggerMessage(EventId = 9234, Level = LogLevel.Error, Message = "Could not read which plugins are disabled per site; no plugin's handler ran for it and outbox deliveries wait for a later drain.")]
    public static partial void StateUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9235, Level = LogLevel.Error, Message = "Could not read which plugins are disabled per site, so job {JobId} ({Type}) did not run; it waits for a later run without using an attempt.")]
    public static partial void JobDeferred(ILogger logger, Exception exception, Guid jobId, string type);

    [LoggerMessage(EventId = 9236, Level = LogLevel.Error, Message = "The MCP provider {Provider} of the plugin {Plugin} failed to list its tools, so the plugin list names none of them.")]
    public static partial void ToolsNotListed(ILogger logger, Exception exception, string provider, string plugin);

    [LoggerMessage(EventId = 9237, Level = LogLevel.Information, Message = "Plugin discovery is off (FluentCMS.Plugins.DisableDiscovery), so the host runs only the plugins it adds with AddPlugin.")]
    public static partial void DiscoveryDisabled(ILogger logger);
}
