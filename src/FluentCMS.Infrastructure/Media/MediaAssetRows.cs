using FluentCMS.Core.Media;

namespace FluentCMS.Infrastructure.Media;

// An asset and its pointers to the latest version and the published one; each version is its own MediaAssetVersionRow.
public sealed class MediaAssetRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public int LatestVersion { get; set; }

    public int? PublishedVersion { get; set; }
}

// A version of an asset that a change set proposed, kept when the set moves on to a newer version, so a version no set ever proposed
// is known to be a person's write in the library (MED-02). Version rows cannot record it themselves, since they never change.
public sealed class ProposedMediaVersionRow
{
    public Guid Id { get; set; }

    public Guid ChangeSetId { get; set; }

    public Guid AssetId { get; set; }

    public int Version { get; set; }
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
