using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Delivery;

internal sealed class PreviewTokenConfiguration : IEntityTypeConfiguration<PreviewTokenRow>
{
    public void Configure(EntityTypeBuilder<PreviewTokenRow> row)
    {
        row.ToTable("PreviewTokens");
        row.HasKey(token => token.Id);
        row.Property(token => token.Id).ValueGeneratedNever();
        row.Property(token => token.TokenHash).IsRequired();

        row.HasOne<SiteRow>().WithMany().HasForeignKey(token => token.SiteId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<ChangeSetRow>().WithMany().HasForeignKey(token => token.ChangeSetId).OnDelete(DeleteBehavior.Restrict);

        // A presented token is found by its hash alone.
        row.HasIndex(token => token.TokenHash).IsUnique();
    }
}
