using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Schema;

internal sealed class ContentTypeVersionConfiguration : IEntityTypeConfiguration<ContentTypeVersionRow>
{
    public void Configure(EntityTypeBuilder<ContentTypeVersionRow> row)
    {
        row.ToTable("ContentTypeVersions", table => table.HasCheckConstraint("CK_ContentTypeVersions_Version", "\"Version\" >= 1"));
        row.HasKey(version => version.Id);
        row.Property(version => version.Id).ValueGeneratedNever();
        row.Property(version => version.Definition).IsRequired();

        row.HasOne<ContentTypeRow>().WithMany().HasForeignKey(version => version.ContentTypeId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(version => new { version.ContentTypeId, version.Version }).IsUnique();
    }
}
