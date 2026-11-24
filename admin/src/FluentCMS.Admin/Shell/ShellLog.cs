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

    [LoggerMessage(EventId = 23, EventName = "AdministrationNotLoaded", Level = LogLevel.Error, Message = "The assembly of the administration pages could not be loaded.")]
    public static partial void AdministrationNotLoaded(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 24, EventName = "SitesNotRead", Level = LogLevel.Warning, Message = "The sites of the user could not be read again; the site switcher shows them as they were.")]
    public static partial void SitesNotRead(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 25, EventName = "PluginsNotRead", Level = LogLevel.Warning, Message = "The plugins of the site could not be read again; the admin shows their contributions as they were.")]
    public static partial void PluginsNotRead(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 26, EventName = "CultureNotKept", Level = LogLevel.Warning, Message = "The browser's storage could not be used for the interface language; the next start is in the browser's.")]
    public static partial void CultureNotKept(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 27, EventName = "CultureNotApplied", Level = LogLevel.Warning, Message = "The app started in another interface language than the saved one again after a reload for it; it stays in this one.")]
    public static partial void CultureNotApplied(ILogger logger);
}
