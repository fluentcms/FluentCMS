using FluentCMS.Core.Media;

namespace FluentCMS.Infrastructure.Media;

// An asset and its pointers to the latest version and the published one; each version is its own MediaAssetVersionRow.
public sealed class MediaAssetRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public int LatestVersion { get; set; }

    public int? PublishedVersion { get; set; }

    // Set by a soft delete and cleared by a restore; every read leaves a deleted asset out (CNT-06).
    public DateTimeOffset? DeletedAt { get; set; }
}

// One version of an asset's metadata with the bytes it points at; inserted once and never changed, which a trigger enforces.
public sealed class MediaAssetVersionRow
{
    public Guid Id { get; set; }

    public Guid AssetId { get; set; }

    public int Version { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Length { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string Storage { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public string? AltText { get; set; }

    public string? Caption { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    internal MediaAssetVersion ToVersion()
    {
        return new MediaAssetVersion(
            AssetId,
            Version,
            new MediaBlob(ContentType, Length, Sha256, Storage, StorageKey),
            new AssetMetadata(FileName, Folder, Tags, AltText, Caption),
            CreatedAt);
    }
}
