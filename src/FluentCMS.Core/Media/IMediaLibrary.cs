using FluentCMS.Core.Results;

namespace FluentCMS.Core.Media;

// The media assets of the site (MED-01, MED-02). Every upload and every metadata change stores a new version that is never changed
// afterwards, and appends its audit event in the same unit of work (CNT-02, AUD-N1).
public interface IMediaLibrary
{
    // The largest upload accepted, in bytes, so a caller can refuse a larger one before reading it.
    long MaxBytes { get; }

    // The bytes go to the configured storage before anything is written; a refused upload deletes them again.
    Task<Result<MediaAsset>> UploadAsync(NewAsset asset, CancellationToken cancellationToken = default);

    // The caller names the version it read; the new version keeps that version's bytes.
    Task<Result<MediaAsset>> UpdateAsync(Guid id, int baseVersion, AssetMetadata metadata, CancellationToken cancellationToken = default);

    Task<Result<MediaAsset>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<MediaAssetVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default);

    // The version readers see: the newest a person wrote in the library after the published one, since those take effect at once,
    // else the published one. A version any change set has proposed goes live only when its set publishes it. Publishing names the
    // version a set will publish, to read what readers see once it has. Null when none is live.
    Task<Result<MediaAssetVersion?>> LiveVersionAsync(Guid id, int? publishing = null, CancellationToken cancellationToken = default);

    // By the latest versions' metadata; oldest asset first.
    Task<IReadOnlyList<MediaAssetSummary>> ListAsync(MediaQuery query, CancellationToken cancellationToken = default);

    // The folders the latest versions sit in, each once, in order; the root is "".
    Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken = default);

    // The bytes of the latest version, or of the version named. The caller disposes the stream.
    Task<Result<MediaContent>> OpenAsync(Guid id, int? version = null, CancellationToken cancellationToken = default);
}

public sealed record NewAsset(Stream Content, string ContentType, AssetMetadata Metadata);

public sealed record MediaQuery(string? Folder = null, bool IncludeSubfolders = false, string? Tag = null)
{
    // Only assets whose file name or folder holds the text, whatever its case.
    public string? Text { get; init; }

    // Only this many of the newest assets, newest first; every asset, oldest first, when null.
    public int? Newest { get; init; }
}

// An asset with its pointers and the latest version.
public sealed record MediaAsset(Guid Id, Guid SiteId, int LatestVersion, int? PublishedVersion, MediaAssetVersion Latest);

// One version: the bytes it points at, as the storage that holds them knows them, and the metadata.
public sealed record MediaAssetVersion(Guid AssetId, int Version, MediaBlob Blob, AssetMetadata Metadata, DateTimeOffset CreatedAt);

public sealed record MediaBlob(string ContentType, long Length, string Sha256, string Storage, string Key);

public sealed record MediaAssetSummary(Guid Id, int LatestVersion, int? PublishedVersion, string FileName, string ContentType, long Length, string Folder, IReadOnlyList<string> Tags, bool HasAltText);

public sealed record MediaContent(Stream Content, string ContentType, string FileName, long Length);

public static class MediaErrorCodes
{
    public const string FileNameInvalid = "file_name_invalid";
    public const string FolderInvalid = "folder_invalid";
    public const string TagInvalid = "tag_invalid";
    public const string AltTextInvalid = "alt_text_invalid";
    public const string CaptionInvalid = "caption_invalid";
    public const string ContentTypeInvalid = "content_type_invalid";
    public const string MediaEmpty = "media_empty";
    public const string MediaTooLarge = "media_too_large";
    public const string ContentMismatch = "media_content_mismatch";
    public const string AssetNotFound = "asset_not_found";
    public const string VersionNotFound = "version_not_found";
    public const string VersionConflict = "version_conflict";
    public const string ContentMissing = "media_content_missing";
    public const string FieldNotMedia = "field_not_media";
}
