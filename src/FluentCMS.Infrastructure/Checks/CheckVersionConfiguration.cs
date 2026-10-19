using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Checks;

internal sealed class CheckVersionConfiguration : IEntityTypeConfiguration<CheckVersionRow>
{
    public void Configure(EntityTypeBuilder<CheckVersionRow> row)
    {
        row.ToTable("CheckVersions", table => table.HasCheckConstraint("CK_CheckVersions_Version", "\"Version\" >= 1"));
        row.HasKey(version => version.Id);
        row.Property(version => version.Id).ValueGeneratedNever();
        row.Property(version => version.Definition).IsRequired();

        row.HasOne<CheckRow>().WithMany().HasForeignKey(version => version.CheckId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(version => new { version.CheckId, version.Version }).IsUnique();
    }
}
