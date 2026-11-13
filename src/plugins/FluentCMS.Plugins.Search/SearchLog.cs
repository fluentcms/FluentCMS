using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Search;

// Event ids 9380 to 9399. A line names the type, the entry and the site, and never holds the text of an entry.
internal static partial class SearchLog
{
    [LoggerMessage(EventId = 9380, Level = LogLevel.Information, Message = "Search rebuild of {Types} in site {SiteId} wrote {Written} rows and removed {Removed} in {DurationMs} ms.")]
    public static partial void Rebuilt(ILogger logger, string types, Guid siteId, int written, int removed, long durationMs);

    [LoggerMessage(EventId = 9381, Level = LogLevel.Information, Message = "Removed {Removed} search rows of the deleted site {SiteId}.")]
    public static partial void SiteRemoved(ILogger logger, int removed, Guid siteId);

    [LoggerMessage(EventId = 9382, Level = LogLevel.Information, Message = "The search plugin was enabled in site {SiteId}, so job {JobId} rebuilds the index of the site.")]
    public static partial void RebuildQueued(ILogger logger, Guid siteId, Guid jobId);

    [LoggerMessage(EventId = 9383, Level = LogLevel.Warning, Message = "Could not read {Type} {DocumentId} in the locale '{Locale}' to index it ({Code}), so the index holds nothing of it in that locale.")]
    public static partial void NotRead(ILogger logger, string type, Guid documentId, string locale, string code);
}
