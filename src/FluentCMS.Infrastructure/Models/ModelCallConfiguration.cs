using FluentCMS.Infrastructure.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Models;

internal sealed class ModelCallConfiguration : IEntityTypeConfiguration<ModelCallRow>
{
    private const int MaxNameLength = 128;

    public void Configure(EntityTypeBuilder<ModelCallRow> row)
    {
        row.ToTable("ModelCalls");
        row.HasKey(call => call.Id);
        row.Property(call => call.Id).ValueGeneratedNever();
        row.Property(call => call.Profile).HasMaxLength(MaxNameLength).IsRequired();
        row.Property(call => call.Provider).HasMaxLength(32).IsRequired();
        row.Property(call => call.Model).HasMaxLength(MaxNameLength).IsRequired();
        row.Property(call => call.ModelVersion).HasMaxLength(MaxNameLength);
        row.Property(call => call.Purpose).HasMaxLength(MaxNameLength).IsRequired();
        row.Property(call => call.InitiatorKind).HasMaxLength(16);
        row.Property(call => call.PromptHash).HasMaxLength(64).IsRequired();
        row.Property(call => call.Cost).HasPrecision(18, 6);

        // Stored as a name, so the table reads without the enum and a reordered enum cannot change stored meaning.
        row.Property(call => call.Outcome).HasConversion<string>().HasMaxLength(16);

        // Credentials are never deleted, so every charged call keeps the credential it counted against.
        row.HasOne<AgentCredentialRow>().WithMany().HasForeignKey(call => call.AgentCredentialId).OnDelete(DeleteBehavior.Restrict);

        // The spend cap sums one credential's calls since a time (SEC-04); an audit event's promptHash finds its call (CHK-05).
        row.HasIndex(call => new { call.AgentCredentialId, call.Timestamp });
        row.HasIndex(call => call.PromptHash);
        row.HasIndex(call => call.Timestamp);
    }
}
