using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Content;

internal static partial class ContentLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "An entry change sink failed for {Kind} of {Type} {Id} in locale '{Locale}'; the write stands.")]
    public static partial void SinkFailed(ILogger logger, Exception exception, EntryChangeKind kind, string type, Guid id, string locale);
}
