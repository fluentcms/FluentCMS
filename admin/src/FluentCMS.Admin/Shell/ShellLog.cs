using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Shell;

// What the browser's console shows of the shell.
internal static partial class ShellLog
{
    [LoggerMessage(EventId = 20, EventName = "ShellStartFailed", Level = LogLevel.Error, Message = "The shell could not read the user, its sites or what it may do in the site.")]
    public static partial void StartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 21, EventName = "ThemeNotKept", Level = LogLevel.Warning, Message = "The browser's storage could not be used for the theme; the choice lasts until the page is left.")]
    public static partial void ThemeNotKept(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 22, EventName = "ColumnsNotKept", Level = LogLevel.Warning, Message = "The browser's storage could not be used for the columns of a list; the choice lasts until the page is left.")]
    public static partial void ColumnsNotKept(ILogger logger, Exception exception);
}
