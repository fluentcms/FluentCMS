using FluentCMS.Core.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Audit;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventRow>
{
    public void Configure(EntityTypeBuilder<AuditEventRow> row)
    {
        // Exactly the agent rows carry a credential, whatever the mapping code does (SEC-01).
        row.ToTable("AuditEvents", table => table.HasCheckConstraint(
            "CK_AuditEvents_AgentCredentialId",
            $"(\"InitiatorKind\" = '{InitiatorKinds.Agent}') = (\"AgentCredentialId\" IS NOT NULL)"));

        row.HasKey(e => e.Id);
        row.Property(e => e.Id).ValueGeneratedNever();
        row.Property(e => e.InitiatorKind).HasMaxLength(16).IsRequired();
        row.Property(e => e.InitiatorName).HasMaxLength(256).IsRequired();
        row.Property(e => e.Operation).HasMaxLength(128).IsRequired();
        row.Property(e => e.SubjectType).HasMaxLength(64);
        row.Property(e => e.Model).HasMaxLength(128);
        row.Property(e => e.PromptHash).HasMaxLength(128);

        row.HasIndex(e => e.Timestamp);

        // The security view lists and counts a few operations across the whole log, newest first.
        row.HasIndex(e => new { e.Operation, e.Timestamp });
        row.HasIndex(e => e.ChangeSetId);
        row.HasIndex(e => e.AgentCredentialId);
        row.HasIndex(e => new { e.SubjectType, e.SubjectId });
    }
}
