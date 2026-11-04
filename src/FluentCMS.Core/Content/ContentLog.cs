using FluentCMS.Content;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Content;

internal static partial class ContentLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "An entry change sink failed for {Kind} of {Type} {Id} in locale '{Locale}'; the write stands.")]
    public static partial void SinkFailed(ILogger logger, Exception exception, EntryChangeKind kind, string type, Guid id, string locale);

    [LoggerMessage(Level = LogLevel.Error, Message = "The events raised during a write could not be dispatched; the write stands.")]
    public static partial void EventsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Item {Index} of a bulk {Operation} of {Type} failed unexpectedly; the other items go on.")]
    public static partial void BulkItemFailed(ILogger logger, Exception exception, int index, BulkOperation operation, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Item {Index} of a bulk {Operation} of {Type} committed, but reading it back failed; it is reported as done.")]
    public static partial void BulkReadBackFailed(ILogger logger, Exception exception, int index, BulkOperation operation, string type);
}
