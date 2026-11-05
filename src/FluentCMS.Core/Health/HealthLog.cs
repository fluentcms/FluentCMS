using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Health;

// Event ids 91xx; the text of an exception is scrubbed before it is logged, because a connection error can quote a
// connection string.
internal static partial class HealthLog
{
    [LoggerMessage(EventId = 9101, Level = LogLevel.Warning, Message = "The readiness check could not reach the database ({ExceptionType}): {Error}")]
    public static partial void DatabaseUnreachable(ILogger logger, string exceptionType, string error);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Warning, Message = "The job runner has not beaten for {Age}; the limit is {Limit}.")]
    public static partial void JobRunnerSilent(ILogger logger, TimeSpan age, TimeSpan limit);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning, Message = "The job runner has not started on this instance.")]
    public static partial void JobRunnerNotStarted(ILogger logger);
}
