using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Checks;

internal sealed class CheckConfiguration : IEntityTypeConfiguration<CheckRow>
{
    public void Configure(EntityTypeBuilder<CheckRow> row)
    {
        row.ToTable("Checks", table =>
        {
            table.HasCheckConstraint("CK_Checks_Version", "\"Version\" >= 1");
            table.HasCheckConstraint(
                "CK_Checks_ActiveVersion",
                "\"ActiveVersion\" IS NULL OR (\"ActiveVersion\" >= 1 AND \"ActiveVersion\" <= \"Version\")");
        });
        row.HasKey(check => check.Id);
        row.Property(check => check.Id).ValueGeneratedNever();
        row.Property(check => check.Name).HasMaxLength(SchemaNames.MaxLength).IsRequired();

        row.HasOne<SiteRow>().WithMany().HasForeignKey(check => check.SiteId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two concurrent creates of one name when both passed the check.
        row.HasIndex(check => new { check.SiteId, check.Name }).IsUnique();
    }
}
