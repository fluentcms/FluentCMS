using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Seeding;

internal static partial class SeedLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Seeder {Seeder} version {Version} was applied.")]
    public static partial void Applied(ILogger logger, string seeder, int version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeder {Seeder} version {Version} is skipped: {Reason}.")]
    public static partial void Skipped(ILogger logger, string seeder, int version, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeder {Seeder} version {Version} failed and will run again at the next start.")]
    public static partial void Failed(ILogger logger, Exception exception, string seeder, int version);
}
