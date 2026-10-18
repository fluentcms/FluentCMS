using FluentCMS.Core.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Sites;

internal sealed class SiteConfiguration : IEntityTypeConfiguration<SiteRow>
{
    public void Configure(EntityTypeBuilder<SiteRow> row)
    {
        row.ToTable("Sites");
        row.HasKey(site => site.Id);
        row.Property(site => site.Id).ValueGeneratedNever();
        row.Property(site => site.Name).HasMaxLength(128).IsRequired();

        // The one site exists from the migration on, so every site-scoped row has a site to point at (SCH-07).
        row.HasData(new SiteRow { Id = Site.Default.Id, Name = Site.Default.Name });
    }
}
