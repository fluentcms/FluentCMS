using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.ChangeSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Checks;

internal sealed class CheckRunConfiguration : IEntityTypeConfiguration<CheckRunRow>
{
    public void Configure(EntityTypeBuilder<CheckRunRow> row)
    {
        row.ToTable("CheckRuns", table => table.HasCheckConstraint("CK_CheckRuns_Number", "\"Number\" >= 1"));
        row.HasKey(run => run.Id);
        row.Property(run => run.Id).ValueGeneratedNever();
        row.Property(run => run.Cost).HasPrecision(18, 6);
        row.Property(run => run.Problems).IsRequired();

        // Stored as names, so the table reads without the enums and a reordered enum cannot change stored meaning.
        row.Property(run => run.Mode).HasConversion<string>().HasMaxLength(16);
        row.Property(run => run.Status).HasConversion<string>().HasMaxLength(16);

        row.HasOne<ChangeSetRow>().WithMany().HasForeignKey(run => run.ChangeSetId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two runs that finish at once from both being a set's run of one number.
        row.HasIndex(run => new { run.ChangeSetId, run.Number }).IsUnique();
    }
}

internal sealed class CheckResultConfiguration : IEntityTypeConfiguration<CheckResultRow>
{
    public void Configure(EntityTypeBuilder<CheckResultRow> row)
    {
        row.ToTable("CheckResults");
        row.HasKey(result => result.Id);
        row.Property(result => result.Id).ValueGeneratedNever();
        row.Property(result => result.ContentType).HasMaxLength(SchemaNames.MaxLength).IsRequired();
        row.Property(result => result.Check).HasMaxLength(SchemaNames.MaxLength).IsRequired();
        row.Property(result => result.CheckType).HasMaxLength(SchemaNames.MaxLength).IsRequired();
        row.Property(result => result.Description).IsRequired();
        row.Property(result => result.Findings).IsRequired();
        row.Property(result => result.Provider).HasMaxLength(32);
        row.Property(result => result.Model).HasMaxLength(128);
        row.Property(result => result.PromptHash).HasMaxLength(64);
        row.Property(result => result.Cost).HasPrecision(18, 6);
        row.Property(result => result.ItemKind).HasConversion<string>().HasMaxLength(16);
        row.Property(result => result.Kind).HasConversion<string>().HasMaxLength(16);
        row.Property(result => result.Severity).HasConversion<string>().HasMaxLength(16);
        row.Property(result => result.Status).HasConversion<string>().HasMaxLength(16);

        row.HasOne<CheckRunRow>().WithMany().HasForeignKey(result => result.RunId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(result => new { result.RunId, result.Position }).IsUnique();
    }
}
