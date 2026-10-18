using FluentCMS.Core.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Content;

internal sealed class EntryReferenceConfiguration : IEntityTypeConfiguration<EntryReferenceRow>
{
    public const int MaxPathLength = 512;

    public void Configure(EntityTypeBuilder<EntryReferenceRow> row)
    {
        row.ToTable("EntryReferences", table => table.HasCheckConstraint("CK_EntryReferences_Version", "\"Version\" >= 1"));
        row.HasKey(reference => reference.Id);
        row.Property(reference => reference.Id).ValueGeneratedNever();
        row.Property(reference => reference.Path).HasMaxLength(MaxPathLength).IsRequired();
        row.Property(reference => reference.TargetKind).HasConversion<string>().HasMaxLength(16);
        row.Property(reference => reference.TargetField).HasMaxLength(SchemaNames.MaxLength);

        row.HasOne<EntryRow>().WithMany().HasForeignKey(reference => reference.EntryId).OnDelete(DeleteBehavior.Restrict);

        // Where used looks up by target (CNT-05); a version's own references are read by entry and version.
        row.HasIndex(reference => new { reference.TargetKind, reference.TargetId, reference.TargetField });
        row.HasIndex(reference => new { reference.EntryId, reference.Version });
    }
}
