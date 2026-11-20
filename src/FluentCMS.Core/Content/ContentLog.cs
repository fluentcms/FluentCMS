using FluentCMS.Content;
using FluentCMS.Core.Content.Hooks;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Content;

// Event ids 94xx.
internal static partial class ContentLog
{
    [LoggerMessage(EventId = 9401, Level = LogLevel.Error, Message = "An entry change sink failed for {Kind} of {Type} {Id} in locale '{Locale}'; the write stands.")]
    public static partial void SinkFailed(ILogger logger, Exception exception, EntryChangeKind kind, string type, Guid id, string locale);

    [LoggerMessage(EventId = 9402, Level = LogLevel.Error, Message = "The events raised during a write could not be dispatched; the write stands.")]
    public static partial void EventsFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9403, Level = LogLevel.Error, Message = "{Code}: item {Index} of a bulk {Operation} of {Type} failed unexpectedly; the other items go on.")]
    public static partial void BulkItemFailed(ILogger logger, Exception exception, string code, int index, BulkOperation operation, string type);

    [LoggerMessage(EventId = 9404, Level = LogLevel.Error, Message = "Item {Index} of a bulk {Operation} of {Type} committed, but reading it back failed; it is reported as done.")]
    public static partial void BulkReadBackFailed(ILogger logger, Exception exception, int index, BulkOperation operation, string type);

    [LoggerMessage(EventId = 9405, Level = LogLevel.Warning, Message = "A write of {Type} {Id} in locale '{Locale}' stored {Field} without a URL with the scheme '{Scheme}', which rich text does not allow; it was stored before that rule.")]
    public static partial void UnsafeUrlStripped(ILogger logger, string type, Guid id, string locale, string field, string scheme);

    [LoggerMessage(EventId = 9406, Level = LogLevel.Error, Message = "The {Kind} hook {Hook} of {Type} did not end within {Timeout}; the write was refused and rolled back.")]
    public static partial void HookTimedOut(ILogger logger, HookKind kind, string hook, string type, TimeSpan timeout);

    [LoggerMessage(EventId = 9407, Level = LogLevel.Warning, Message = "The {Kind} hook {Hook} of {Type} took {Elapsed}, inside the transaction of the write.")]
    public static partial void SlowHook(ILogger logger, HookKind kind, string hook, string type, TimeSpan elapsed);
}
