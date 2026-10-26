using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Media;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAssetRow>
{
    public void Configure(EntityTypeBuilder<MediaAssetRow> row)
    {
        row.ToTable("MediaAssets", table =>
        {
            table.HasCheckConstraint("CK_MediaAssets_LatestVersion", "\"LatestVersion\" >= 1");
            table.HasCheckConstraint(
                "CK_MediaAssets_PublishedVersion",
                "\"PublishedVersion\" IS NULL OR (\"PublishedVersion\" >= 1 AND \"PublishedVersion\" <= \"LatestVersion\")");
        });
        row.HasKey(asset => asset.Id);
        row.Property(asset => asset.Id).ValueGeneratedNever();

        row.HasOne<SiteRow>().WithMany().HasForeignKey(asset => asset.SiteId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MediaAssetVersionConfiguration : IEntityTypeConfiguration<MediaAssetVersionRow>
{
    // Created by the MediaAssets migration on each provider. A migration that rebuilds the table on SQLite must create it again.
    public const string NoUpdateTrigger = "TR_MediaAssetVersions_NoUpdate";

    public void Configure(EntityTypeBuilder<MediaAssetVersionRow> row)
    {
        row.ToTable("MediaAssetVersions", table =>
        {
            table.HasCheckConstraint("CK_MediaAssetVersions_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_MediaAssetVersions_Length", "\"Length\" > 0");
            table.HasTrigger(NoUpdateTrigger);
        });
        row.HasKey(version => version.Id);
        row.Property(version => version.Id).ValueGeneratedNever();
        row.Property(version => version.FileName).HasMaxLength(AssetMetadata.MaxFileNameLength).IsRequired();
        row.Property(version => version.ContentType).HasMaxLength(127).IsRequired();
        row.Property(version => version.Sha256).HasMaxLength(64).IsRequired();
        row.Property(version => version.Storage).HasMaxLength(SchemaNames.MaxLength).IsRequired();
        row.Property(version => version.StorageKey).HasMaxLength(256).IsRequired();
        row.Property(version => version.Folder).HasMaxLength(AssetMetadata.MaxFolderLength).IsRequired();
        row.Property(version => version.AltText).HasMaxLength(AssetMetadata.MaxAltTextLength);
        row.Property(version => version.Caption).HasMaxLength(AssetMetadata.MaxCaptionLength);

        row.HasOne<MediaAssetRow>().WithMany().HasForeignKey(version => version.AssetId).OnDelete(DeleteBehavior.Restrict);

        row.HasIndex(version => new { version.AssetId, version.Version }).IsUnique();
        row.HasIndex(version => version.Folder);
    }
}

internal sealed class ProposedMediaVersionConfiguration : IEntityTypeConfiguration<ProposedMediaVersionRow>
{
    public void Configure(EntityTypeBuilder<ProposedMediaVersionRow> row)
    {
        row.ToTable("ProposedMediaVersions", table => table.HasCheckConstraint("CK_ProposedMediaVersions_Version", "\"Version\" >= 1"));
        row.HasKey(proposed => proposed.Id);
        row.Property(proposed => proposed.Id).ValueGeneratedNever();

        row.HasOne<ChangeSetRow>().WithMany().HasForeignKey(proposed => proposed.ChangeSetId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<MediaAssetRow>().WithMany().HasForeignKey(proposed => proposed.AssetId).OnDelete(DeleteBehavior.Restrict);

        row.HasIndex(proposed => new { proposed.AssetId, proposed.Version, proposed.ChangeSetId }).IsUnique();
    }
}
