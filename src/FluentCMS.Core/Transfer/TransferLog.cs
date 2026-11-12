using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Transfer;

// Event ids 9180 to 9199.
internal static partial class TransferLog
{
    [LoggerMessage(EventId = 9180, Level = LogLevel.Information, Message = "Export of site {Site} started.")]
    public static partial void ExportStarted(ILogger logger, Guid site);

    [LoggerMessage(EventId = 9181, Level = LogLevel.Information, Message = "Export of site {Site} finished with {Entries} entries.")]
    public static partial void ExportFinished(ILogger logger, Guid site, int entries);

    [LoggerMessage(EventId = 9182, Level = LogLevel.Warning, Message = "Export of site {Site} stopped before the package was complete.")]
    public static partial void ExportStopped(ILogger logger, Exception exception, Guid site);

    [LoggerMessage(EventId = 9183, Level = LogLevel.Information, Message = "Import into site {Site} started under correlation id {CorrelationId}, dry run {DryRun}.")]
    public static partial void ImportStarted(ILogger logger, Guid site, string correlationId, bool dryRun);

    [LoggerMessage(EventId = 9184, Level = LogLevel.Information, Message = "Import into site {Site} finished: {Created} created, {Updated} updated, {Skipped} skipped, {Conflicts} conflicts, {Errors} errors.")]
    public static partial void ImportFinished(ILogger logger, Guid site, int created, int updated, int skipped, int conflicts, int errors);

    [LoggerMessage(EventId = 9185, Level = LogLevel.Error, Message = "{Code}: item {Id} of {Section} failed unexpectedly; the import goes on.")]
    public static partial void ItemFailed(ILogger logger, Exception exception, string code, string section, string id);

    [LoggerMessage(EventId = 9186, Level = LogLevel.Warning, Message = "Import into site {Site} stopped before it was complete; running it again finishes it.")]
    public static partial void ImportStopped(ILogger logger, Exception exception, Guid site);

    [LoggerMessage(EventId = 9187, Level = LogLevel.Warning, Message = "The transfer activity row of site {Site} could not be written.")]
    public static partial void ActivityFailed(ILogger logger, Exception exception, Guid site);
}
