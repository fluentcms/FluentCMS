using FluentCMS.Core.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Agents;

internal sealed class ToolCallConfiguration : IEntityTypeConfiguration<ToolCallRow>
{
    // MCP limits tool names to 128 characters.
    private const int MaxToolLength = 128;

    public void Configure(EntityTypeBuilder<ToolCallRow> row)
    {
        row.ToTable("ToolCalls");
        row.HasKey(call => call.Id);
        row.Property(call => call.Id).ValueGeneratedNever();
        row.Property(call => call.AgentName).HasMaxLength(AgentCredential.MaxDisplayNameLength).IsRequired();
        row.Property(call => call.Tool).HasMaxLength(MaxToolLength).IsRequired();
        row.Property(call => call.Model).HasMaxLength(ToolCall.MaxDeclaredLength);
        row.Property(call => call.TaskId).HasMaxLength(ToolCall.MaxDeclaredLength);

        // Stored as a name, so the table reads without the enum and a reordered enum cannot change stored meaning.
        row.Property(call => call.Outcome).HasConversion<string>().HasMaxLength(16);

        // Credentials are never deleted, so every call keeps the credential it was made under.
        row.HasOne<AgentCredentialRow>().WithMany().HasForeignKey(call => call.AgentCredentialId).OnDelete(DeleteBehavior.Restrict);

        // The calls-per-period cap counts one credential's calls since a time (SEC-04).
        row.HasIndex(call => new { call.AgentCredentialId, call.Timestamp });
    }
}
