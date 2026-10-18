using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Schema;

internal sealed class ContentTypeConfiguration : IEntityTypeConfiguration<ContentTypeRow>
{
    public void Configure(EntityTypeBuilder<ContentTypeRow> row)
    {
        row.ToTable("ContentTypes", table =>
        {
            table.HasCheckConstraint("CK_ContentTypes_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_ContentTypes_PublishedVersion", "\"PublishedVersion\" IS NULL OR \"PublishedVersion\" >= 1");
        });
        row.HasKey(type => type.Id);
        row.Property(type => type.Id).ValueGeneratedNever();
        row.Property(type => type.Name).HasMaxLength(ContentType.MaxNameLength).IsRequired();
        row.Property(type => type.Slug).HasMaxLength(SchemaNames.MaxLength).IsRequired();

        row.HasOne<SiteRow>().WithMany().HasForeignKey(type => type.SiteId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two concurrent creates of one slug when both passed the check (SCH-07).
        row.HasIndex(type => new { type.SiteId, type.Slug }).IsUnique();
    }
}
