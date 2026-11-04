using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Settings;

// Nothing here takes a value of a setting: a log line names the area, the scope and the actor.
internal static partial class SettingsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The stored settings of area '{Area}' for {Scope} cannot be read by the class as it is now; the next level is used instead.")]
    public static partial void Unreadable(ILogger logger, string area, string scope);

    [LoggerMessage(Level = LogLevel.Information, Message = "The settings of area '{Area}' for {Scope} were {Change} by {Actor}.")]
    public static partial void Changed(ILogger logger, string area, string scope, string change, Guid actor);
}
