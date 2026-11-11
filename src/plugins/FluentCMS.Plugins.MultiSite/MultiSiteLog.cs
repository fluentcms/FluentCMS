using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.MultiSite;

// Event ids 9340 to 9359.
internal static partial class MultiSiteLog
{
    [LoggerMessage(EventId = 9340, Level = LogLevel.Information, Message = "The host name {Hostname} was added to the site {SiteId}.")]
    public static partial void HostAdded(ILogger logger, string hostname, Guid siteId);

    [LoggerMessage(EventId = 9341, Level = LogLevel.Information, Message = "The host name {Hostname} is now the primary host of the site {SiteId}.")]
    public static partial void HostMadePrimary(ILogger logger, string hostname, Guid siteId);

    [LoggerMessage(EventId = 9342, Level = LogLevel.Information, Message = "The host name {Hostname} was removed from the site {SiteId}.")]
    public static partial void HostRemoved(ILogger logger, string hostname, Guid siteId);

    [LoggerMessage(EventId = 9343, Level = LogLevel.Information, Message = "Removed {Count} host names of the deleted site {SiteId}.")]
    public static partial void HostsOfDeletedSiteRemoved(ILogger logger, int count, Guid siteId);

    [LoggerMessage(EventId = 9344, Level = LogLevel.Warning, Message = "The host name {Hostname} was held by the site {SiteId}, which no longer exists, and was released.")]
    public static partial void OrphanHostReleased(ILogger logger, string hostname, Guid siteId);
}
