using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Administration.Settings;

// What the browser's console shows of the settings pages. Nothing of a document is logged, so no secret reaches a log.
internal static partial class SettingsLog
{
    [LoggerMessage(EventId = 70, EventName = "SettingsPageFailed", Level = LogLevel.Error, Message = "The settings page {Type} of the admin half {Name} could not be shown.")]
    public static partial void PageFailed(ILogger logger, string name, string type, Exception exception);
}
