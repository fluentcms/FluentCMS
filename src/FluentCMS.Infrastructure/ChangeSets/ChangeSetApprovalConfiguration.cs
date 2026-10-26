using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.ChangeSets;

internal sealed class ChangeSetApprovalConfiguration : IEntityTypeConfiguration<ChangeSetApprovalRow>
{
    public void Configure(EntityTypeBuilder<ChangeSetApprovalRow> row)
    {
        row.ToTable("ChangeSetApprovals");

        // One approval per person, also when the same person approves twice at once.
        row.HasKey(approval => new { approval.ChangeSetId, approval.UserId });
        row.Property(approval => approval.ApproverName).HasMaxLength(256).IsRequired();

        row.HasOne<ChangeSetRow>().WithMany().HasForeignKey(approval => approval.ChangeSetId).OnDelete(DeleteBehavior.Restrict);
    }
}
