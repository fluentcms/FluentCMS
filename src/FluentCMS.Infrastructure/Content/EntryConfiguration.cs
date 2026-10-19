using FluentCMS.Infrastructure.Schema;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Content;

internal sealed class EntryConfiguration : IEntityTypeConfiguration<EntryRow>
{
    public void Configure(EntityTypeBuilder<EntryRow> row)
    {
        row.ToTable("Entries", table =>
        {
            table.HasCheckConstraint("CK_Entries_LatestVersion", "\"LatestVersion\" >= 1");

            // Publish points at a version that was written, so never past the latest draft.
            table.HasCheckConstraint(
                "CK_Entries_PublishedVersion",
                "\"PublishedVersion\" IS NULL OR (\"PublishedVersion\" >= 1 AND \"PublishedVersion\" <= \"LatestVersion\")");

            table.HasCheckConstraint("CK_Entries_State", "\"State\" IN ('Draft', 'Published', 'Reverted', 'Archived')");
        });
        row.HasKey(entry => entry.Id);
        row.Property(entry => entry.Id).ValueGeneratedNever();
        row.Property(entry => entry.State).HasConversion<string>().HasMaxLength(16);

        row.HasOne<SiteRow>().WithMany().HasForeignKey(entry => entry.SiteId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<ContentTypeRow>().WithMany().HasForeignKey(entry => entry.ContentTypeId).OnDelete(DeleteBehavior.Restrict);

        row.HasIndex(entry => new { entry.SiteId, entry.ContentTypeId });
    }
}
