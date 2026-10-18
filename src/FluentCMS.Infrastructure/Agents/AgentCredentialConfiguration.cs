using FluentCMS.Core.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Agents;

internal sealed class AgentCredentialConfiguration : IEntityTypeConfiguration<AgentCredentialRow>
{
    public void Configure(EntityTypeBuilder<AgentCredentialRow> row)
    {
        // No credential is unlimited, whatever the mapping code does (SEC-04).
        row.ToTable("AgentCredentials", table => table.HasCheckConstraint(
            "CK_AgentCredentials_Caps",
            "\"CallsPerPeriod\" >= 1 AND \"ItemsPerChangeSet\" >= 1 AND \"ChangeSetsPerDay\" >= 1"));

        row.HasKey(credential => credential.Id);
        row.Property(credential => credential.Id).ValueGeneratedNever();
        row.Property(credential => credential.DisplayName).HasMaxLength(AgentCredential.MaxDisplayNameLength).IsRequired();
        row.Property(credential => credential.KeyHash).IsRequired();

        // Stored as names, so the table reads without the enum and a reordered enum cannot change stored meaning.
        row.PrimitiveCollection(credential => credential.Operations).ElementType(element => element.HasConversion<string>());

        // A presented key is found by its hash alone.
        row.HasIndex(credential => credential.KeyHash).IsUnique();
    }
}
