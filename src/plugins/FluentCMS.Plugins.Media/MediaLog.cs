using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Media;

// Event ids 9260 to 9279.
internal static partial class MediaLog
{
    [LoggerMessage(EventId = 9260, Level = LogLevel.Warning, Message = "The file {Key} of the {Storage} storage, written for an upload that failed, could not be deleted; it holds no asset and can be removed.")]
    public static partial void NotDeleted(ILogger logger, string storage, string key, Exception exception);

    [LoggerMessage(EventId = 9261, Level = LogLevel.Information, Message = "The media signing key was created and stored, protected by Data Protection.")]
    public static partial void SigningKeyCreated(ILogger logger);

    [LoggerMessage(EventId = 9262, Level = LogLevel.Error, Message = "The media signing key could not be unprotected; the Data Protection key that protected it is missing from the key ring.")]
    public static partial void SigningKeyUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9263, Level = LogLevel.Debug, Message = "A variant {Hash} of the media asset {AssetId} was made: {MimeType}, {Size} bytes.")]
    public static partial void VariantCreated(ILogger logger, Guid assetId, string hash, string mimeType, long size);

    [LoggerMessage(EventId = 9264, Level = LogLevel.Warning, Message = "The variant {Key} of the {Storage} storage, of a deleted media asset, could not be deleted; the cleanup tries again on its next run.")]
    public static partial void VariantNotDeleted(ILogger logger, string storage, string key, Exception? exception);

    [LoggerMessage(EventId = 9265, Level = LogLevel.Information, Message = "The variant cleanup removed {Count} variants of deleted media assets.")]
    public static partial void VariantsRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 9266, Level = LogLevel.Warning, Message = "The media signing key could not be read or created at startup; it is tried again.")]
    public static partial void SigningKeyNotReady(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9267, Level = LogLevel.Warning, Message = "The schedule of the media variant cleanup could not be stored; it is tried again.")]
    public static partial void CleanupNotScheduled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9268, Level = LogLevel.Warning, Message = "The file {Key} of the {Storage} storage, left by the media asset {AssetId} when its file was replaced, could not be deleted; it holds no asset and can be removed.")]
    public static partial void ReplacedNotDeleted(ILogger logger, Guid assetId, string storage, string key, Exception? exception);

    [LoggerMessage(EventId = 9269, Level = LogLevel.Warning, Message = "The file {Key} of the {Storage} storage, of the media asset {AssetId}, deleted longer ago than the retention, could not be deleted; the cleanup tries again on its next run.")]
    public static partial void AssetNotPurged(ILogger logger, Guid assetId, string storage, string key, Exception? exception);

    [LoggerMessage(EventId = 9270, Level = LogLevel.Information, Message = "The cleanup purged {Count} media assets deleted longer ago than the retention.")]
    public static partial void AssetsPurged(ILogger logger, int count);
}
