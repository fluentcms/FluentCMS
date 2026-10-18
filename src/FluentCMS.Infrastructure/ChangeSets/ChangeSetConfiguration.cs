using FluentCMS.Core.ChangeSets;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.ChangeSets;

internal sealed class ChangeSetConfiguration : IEntityTypeConfiguration<ChangeSetRow>
{
    public void Configure(EntityTypeBuilder<ChangeSetRow> row)
    {
        // A set carries a rejection reason exactly when it is rejected, whatever the mapping code does (CHG-08).
        row.ToTable("ChangeSets", table => table.HasCheckConstraint(
            "CK_ChangeSets_RejectionReason",
            $"(\"State\" = '{ChangeSetState.Rejected}') = (\"RejectionReason\" IS NOT NULL)"));

        row.HasKey(set => set.Id);
        row.Property(set => set.Id).ValueGeneratedNever();
        row.Property(set => set.Rationale).IsRequired();
        row.Property(set => set.InitiatorKind).HasMaxLength(16).IsRequired();
        row.Property(set => set.InitiatorName).HasMaxLength(256).IsRequired();
        row.Property(set => set.SessionId).HasMaxLength(ChangeSet.MaxSessionIdLength);
        row.Property(set => set.State).HasConversion<string>().HasMaxLength(32);

        row.HasOne<SiteRow>().WithMany().HasForeignKey(set => set.SiteId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<ChangeSetRow>().WithMany().HasForeignKey(set => set.RevertsChangeSetId).OnDelete(DeleteBehavior.Restrict);

        row.HasIndex(set => set.State);

        // A session has at most one open set, also when two of its first operations arrive at once (CHG-02).
        row.HasIndex(set => new { set.InitiatorKind, set.InitiatorId, set.SessionId })
            .IsUnique()
            .HasFilter($"\"State\" = '{ChangeSetState.Open}' AND \"SessionId\" IS NOT NULL");
    }
}
