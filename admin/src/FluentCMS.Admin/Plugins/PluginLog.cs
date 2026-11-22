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

    [LoggerMessage(EventId = 33, EventName = "PluginComponentNotCreated", Level = LogLevel.Error, Message = "The component {Type} of the admin half {Name} cannot be created: {Problem}")]
    public static partial void ComponentNotCreated(ILogger logger, string name, string type, string problem);

    [LoggerMessage(EventId = 34, EventName = "PluginBulkActionNotCreated", Level = LogLevel.Error, Message = "The bulk action {Type} of the admin half {Name} could not be created.")]
    public static partial void BulkActionNotCreated(ILogger logger, string name, string type, Exception exception);

    [LoggerMessage(EventId = 35, EventName = "PluginBulkActionFailed", Level = LogLevel.Error, Message = "The bulk action {Type} of the admin half {Name} failed.")]
    public static partial void BulkActionFailed(ILogger logger, string name, string type, Exception exception);

    [LoggerMessage(EventId = 36, EventName = "PluginFieldActionNotCreated", Level = LogLevel.Error, Message = "The field action {Type} of the admin half {Name} could not be created.")]
    public static partial void FieldActionNotCreated(ILogger logger, string name, string type, Exception exception);

    [LoggerMessage(EventId = 37, EventName = "PluginFieldActionFailed", Level = LogLevel.Error, Message = "The field action {Type} of the admin half {Name} failed.")]
    public static partial void FieldActionFailed(ILogger logger, string name, string type, Exception exception);

    [LoggerMessage(EventId = 39, EventName = "PluginFieldActionValueRefused", Level = LogLevel.Error, Message = "The field action {Type} of the admin half {Name} gave a value that is not one of a {FieldType} field, so the draft is left as it was.")]
    public static partial void FieldActionValueRefused(ILogger logger, string name, string type, string fieldType);

    [LoggerMessage(EventId = 38, EventName = "PluginFieldsNotRead", Level = LogLevel.Warning, Message = "The fields of the field actions of {Path} could not be read, so they are offered on none.")]
    public static partial void FieldsNotRead(ILogger logger, string path, Exception exception);
}
