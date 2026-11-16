using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Storage;

internal static partial class MigrationLog
{
    [LoggerMessage(EventId = 9301, Level = LogLevel.Information, Message = "Applying {Count} migrations of the {Stream} stream.")]
    public static partial void ApplyingStream(ILogger logger, int count, string stream);

    [LoggerMessage(EventId = 9302, Level = LogLevel.Information, Message = "Schema sync applied: {Change}.")]
    public static partial void SchemaSyncApplied(ILogger logger, string change);

    [LoggerMessage(EventId = 9303, Level = LogLevel.Warning, Message = "Schema sync did not apply: {Change}. Use a migration, or recreate the development database.")]
    public static partial void SchemaSyncNotApplied(ILogger logger, string change);

    [LoggerMessage(EventId = 9304, Level = LogLevel.Warning, Message = "{Key} is Auto, but schema sync runs only in Development on SQLite, or where the host allows it with AllowSchemaSyncOutsideDevelopment; the content stream applies instead.")]
    public static partial void SchemaSyncRefused(ILogger logger, string key);

    [LoggerMessage(EventId = 9305, Level = LogLevel.Warning, Message = "The {Stream} stream waits for EF Core's migrations lock, the row of __EFMigrationsLock written at {Written}; {Fix}.")]
    public static partial void WaitingForMigrationsLock(ILogger logger, string stream, string written, string fix);
}
