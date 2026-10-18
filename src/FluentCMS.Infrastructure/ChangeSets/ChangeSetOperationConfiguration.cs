using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.ChangeSets;

internal sealed class ChangeSetOperationConfiguration : IEntityTypeConfiguration<ChangeSetOperationRow>
{
    public void Configure(EntityTypeBuilder<ChangeSetOperationRow> row)
    {
        row.ToTable("ChangeSetOperations");
        row.HasKey(operation => operation.Id);
        row.Property(operation => operation.Id).ValueGeneratedNever();
        row.Property(operation => operation.Kind).HasConversion<string>().HasMaxLength(16);
        row.Property(operation => operation.ItemKind).HasConversion<string>().HasMaxLength(16);

        row.HasOne<ChangeSetRow>().WithMany().HasForeignKey(operation => operation.ChangeSetId).OnDelete(DeleteBehavior.Restrict);

        row.HasIndex(operation => new { operation.ChangeSetId, operation.Position }).IsUnique();

        // One operation per item in a set (CHG-03, CHG-06); the second index finds the sets that touch an item.
        row.HasIndex(operation => new { operation.ChangeSetId, operation.ItemKind, operation.ItemId }).IsUnique();
        row.HasIndex(operation => new { operation.ItemKind, operation.ItemId });
    }
}
