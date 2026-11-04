using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Seeding;

internal static partial class SeedLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeder {Seeder} version {Version} was applied.")]
    public static partial void Applied(ILogger logger, string seeder, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding is skipped: {Reason}. It is tried again at a later start or with ISeedRunner.")]
    public static partial void Skipped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeder {Seeder} version {Version} failed and will run again at the next start.")]
    public static partial void Failed(ILogger logger, Exception exception, string seeder, int version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The claim on seeding was taken over by another run; this run stops and leaves the settings as they are.")]
    public static partial void ClaimLost(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The claim on seeding could not be renewed.")]
    public static partial void HeartbeatFailed(ILogger logger, Exception exception);
}
