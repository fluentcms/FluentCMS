using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// Event ids 9260 to 9279.
internal static partial class MediaLog
{
    [LoggerMessage(EventId = 9260, Level = LogLevel.Warning, Message = "The file {Key} of the {Storage} storage, written for an upload that failed, could not be deleted; it holds no asset and can be removed.")]
    public static partial void NotDeleted(ILogger logger, string storage, string key, Exception exception);
}
