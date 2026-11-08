using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Media;

// An asset of a site. Alt and Caption are JSON maps of locale to text, and Tags a JSON array. The bytes are in the storage
// named by StorageProvider under StorageKey, so that assets written before a change of storage stay where they are.
internal sealed class Asset
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid? FolderId { get; set; }

    public string FileName { get; set; } = "";

    public string StorageProvider { get; set; } = "";

    public string StorageKey { get; set; } = "";

    public string MimeType { get; set; } = "";

    public long Size { get; set; }

    public string Checksum { get; set; } = "";

    public int? Width { get; set; }

    public int? Height { get; set; }

    // Seconds of audio or video, which no upload reads yet.
    public double? Duration { get; set; }

    public string? Title { get; set; }

    public string Alt { get; set; } = "{}";

    public string Caption { get; set; } = "{}";

    public string? Credit { get; set; }

    public string Tags { get; set; } = "[]";

    public double? FocalX { get; set; }

    public double? FocalY { get; set; }

    public bool IsPrivate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

// A folder of a site. Path is the ids from the root down to the folder itself, each followed by a slash and the whole
// started by one, such as "/{root}/{child}/", so that a folder's subtree is the folders whose path starts with its own.
internal sealed class Folder
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid? ParentId { get; set; }

    public string Name { get; set; } = "";

    public string Path { get; set; } = "";
}

// A transformed image of an asset, in the asset's storage under StorageKey. ParamsHash is the SHA-256 of the transform as
// resolved, the asset's checksum included, so a request under any file name, or with a preset that resolves alike, finds it.
internal sealed class Variant
{
    public Guid AssetId { get; set; }

    public string ParamsHash { get; set; } = "";

    public string StorageKey { get; set; } = "";

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

// A named set of transform parameters of a site. Params is a JSON object of the query's names and values, such as
// {"w":"1200","fit":"cover"}.
internal sealed class Preset
{
    public Guid SiteId { get; set; }

    public string Name { get; set; } = "";

    public string Params { get; set; } = "{}";
}

// The one signing key of the instance, protected by Data Protection, in the row whose id is MediaSigning.KeyId.
internal sealed class SigningKey
{
    public int Id { get; set; }

    public string Key { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class MediaModel : IPluginModelContributor
{
    public const string AssetTable = "cms_media_asset";
    public const string FolderTable = "cms_media_folder";
    public const string VariantTable = "cms_media_variant";
    public const string PresetTable = "cms_media_preset";
    public const string SigningKeyTable = "cms_media_signing_key";

    public const int FileNameMaxLength = 255;
    public const int StorageProviderMaxLength = 64;
    public const int StorageKeyMaxLength = 512;
    public const int MimeTypeMaxLength = 128;
    public const int TitleMaxLength = 256;
    public const int CreditMaxLength = 256;
    public const int FolderNameMaxLength = 128;

    // Thirty-seven characters a level, so a folder is at most twenty levels deep.
    public const int PathMaxLength = 1024;
    public const int MaxDepth = 20;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>(asset =>
        {
            asset.ToTable(AssetTable);
            asset.HasKey(item => item.Id);
            asset.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            asset.Property(item => item.SiteId).HasColumnName("site_id");
            asset.Property(item => item.FolderId).HasColumnName("folder_id");
            asset.Property(item => item.FileName).HasColumnName("file_name").HasMaxLength(FileNameMaxLength).IsRequired();
            asset.Property(item => item.StorageProvider).HasColumnName("storage_provider").HasMaxLength(StorageProviderMaxLength).IsRequired();
            asset.Property(item => item.StorageKey).HasColumnName("storage_key").HasMaxLength(StorageKeyMaxLength).IsRequired();
            asset.Property(item => item.MimeType).HasColumnName("mime_type").HasMaxLength(MimeTypeMaxLength).IsRequired();
            asset.Property(item => item.Size).HasColumnName("size");
            asset.Property(item => item.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
            asset.Property(item => item.Width).HasColumnName("width");
            asset.Property(item => item.Height).HasColumnName("height");
            asset.Property(item => item.Duration).HasColumnName("duration");
            asset.Property(item => item.Title).HasColumnName("title").HasMaxLength(TitleMaxLength);
            asset.Property(item => item.Alt).HasColumnName("alt").IsRequired();
            asset.Property(item => item.Caption).HasColumnName("caption").IsRequired();
            asset.Property(item => item.Credit).HasColumnName("credit").HasMaxLength(CreditMaxLength);
            asset.Property(item => item.Tags).HasColumnName("tags").IsRequired();
            asset.Property(item => item.FocalX).HasColumnName("focal_x");
            asset.Property(item => item.FocalY).HasColumnName("focal_y");
            asset.Property(item => item.IsPrivate).HasColumnName("is_private");
            asset.Property(item => item.CreatedAt).HasColumnName("created_at");
            asset.Property(item => item.CreatedBy).HasColumnName("created_by");
            asset.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            asset.Property(item => item.UpdatedBy).HasColumnName("updated_by");
            asset.Property(item => item.DeletedAt).HasColumnName("deleted_at");
            asset.HasIndex(item => new { item.SiteId, item.FolderId }).HasDatabaseName("ix_cms_media_asset_site_folder");
            asset.HasIndex(item => new { item.SiteId, item.CreatedAt }).HasDatabaseName("ix_cms_media_asset_site_created_at");
        });

        modelBuilder.Entity<Folder>(folder =>
        {
            folder.ToTable(FolderTable);
            folder.HasKey(item => item.Id);
            folder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            folder.Property(item => item.SiteId).HasColumnName("site_id");
            folder.Property(item => item.ParentId).HasColumnName("parent_id");
            folder.Property(item => item.Name).HasColumnName("name").HasMaxLength(FolderNameMaxLength).IsRequired();
            folder.Property(item => item.Path).HasColumnName("path").HasMaxLength(PathMaxLength).IsRequired();

            // Two folders at the root share a null parent, which the index does not compare, so the service checks those.
            folder.HasIndex(item => new { item.SiteId, item.ParentId, item.Name }).IsUnique().HasDatabaseName("ux_cms_media_folder_site_parent_name");
            folder.HasIndex(item => new { item.SiteId, item.Path }).HasDatabaseName("ix_cms_media_folder_site_path");
        });

        modelBuilder.Entity<Variant>(variant =>
        {
            variant.ToTable(VariantTable);
            variant.HasKey(item => new { item.AssetId, item.ParamsHash });
            variant.Property(item => item.AssetId).HasColumnName("asset_id");
            variant.Property(item => item.ParamsHash).HasColumnName("params_hash").HasMaxLength(64).IsRequired();
            variant.Property(item => item.StorageKey).HasColumnName("storage_key").HasMaxLength(StorageKeyMaxLength).IsRequired();
            variant.Property(item => item.Size).HasColumnName("size");
            variant.Property(item => item.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<Preset>(preset =>
        {
            preset.ToTable(PresetTable);
            preset.HasKey(item => new { item.SiteId, item.Name });
            preset.Property(item => item.SiteId).HasColumnName("site_id");
            preset.Property(item => item.Name).HasColumnName("name").HasMaxLength(ImageQuery.PresetNameMaxLength).IsRequired();
            preset.Property(item => item.Params).HasColumnName("params").IsRequired();
        });

        modelBuilder.Entity<SigningKey>(key =>
        {
            key.ToTable(SigningKeyTable);
            key.HasKey(item => item.Id);
            key.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            key.Property(item => item.Key).HasColumnName("key").IsRequired();
            key.Property(item => item.CreatedAt).HasColumnName("created_at");
        });
    }
}
