using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Entities;

internal sealed class EntityFactConfiguration : IEntityTypeConfiguration<EntityFactRow>
{
    public void Configure(EntityTypeBuilder<EntityFactRow> row)
    {
        row.ToTable("EntityFacts", table =>
        {
            table.HasCheckConstraint("CK_EntityFacts_Version", "\"Version\" >= 1");

            // The flag is the source's absence, stored so routing filters on a column (ENT-02).
            table.HasCheckConstraint("CK_EntityFacts_Unsourced", "\"Unsourced\" = (\"SourceKind\" IS NULL)");
        });
        row.HasKey(fact => fact.Id);
        row.Property(fact => fact.Id).ValueGeneratedNever();
        row.Property(fact => fact.Field).HasMaxLength(SchemaNames.MaxLength).IsRequired();
        row.Property(fact => fact.SourceKind).HasMaxLength(16);

        row.HasOne<EntryRow>().WithMany().HasForeignKey(fact => fact.EntryId).OnDelete(DeleteBehavior.Restrict);

        row.HasIndex(fact => new { fact.EntryId, fact.Version, fact.Field }).IsUnique();
        row.HasIndex(fact => fact.Unsourced);
        row.HasIndex(fact => fact.ValidUntil);
        row.HasIndex(fact => fact.LastVerified);
    }
}
