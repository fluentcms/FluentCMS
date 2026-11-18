using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Plugins;

// What the browser's console shows of the admin halves.
internal static partial class PluginLog
{
    [LoggerMessage(EventId = 30, EventName = "PluginNotLoaded", Level = LogLevel.Warning, Message = "The admin half {Name} ({Assembly}) is not loaded: {Kind}.")]
    public static partial void NotLoaded(ILogger logger, string? name, string? assembly, PluginProblemKind kind);

    [LoggerMessage(EventId = 31, EventName = "PluginLoadFailed", Level = LogLevel.Error, Message = "The admin half {Name} could not be loaded.")]
    public static partial void LoadFailed(ILogger logger, string name, Exception exception);

    [LoggerMessage(EventId = 32, EventName = "PluginWidgetFailed", Level = LogLevel.Error, Message = "The widget {Type} of the admin half {Name} could not be shown.")]
    public static partial void WidgetFailed(ILogger logger, string name, string type, Exception exception);
}
