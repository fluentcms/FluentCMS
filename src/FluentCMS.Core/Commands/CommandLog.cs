using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Commands;

internal static partial class CommandLog
{
    [LoggerMessage(EventId = 9170, Level = LogLevel.Information, Message = "Running the command {Command}.")]
    public static partial void Running(ILogger logger, string command);

    [LoggerMessage(EventId = 9171, Level = LogLevel.Information, Message = "The command {Command} ended with exit code {ExitCode}.")]
    public static partial void Ended(ILogger logger, string command, int exitCode);

    // The exception as text that passed through SecretScrubber, since a driver may have put a secret in it.
    [LoggerMessage(EventId = 9172, Level = LogLevel.Error, Message = "The command {Command} failed ({ExceptionType}): {Exception}")]
    public static partial void Failed(ILogger logger, string command, string exceptionType, string exception);
}
