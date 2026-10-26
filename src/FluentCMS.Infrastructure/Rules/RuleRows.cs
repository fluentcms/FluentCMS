using FluentCMS.Core.Rules;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Checks;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Rules;

// A rule, the number of its latest version and of its active one; each version's definition is its own RuleVersionRow.
public sealed class RuleRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public int Version { get; set; }

    // Moved only by publishing a change set (RUL-01): null while a draft or retired.
    public int? ActiveVersion { get; set; }

    public RuleState State { get; set; }
}

// One version of a rule's definition; inserted once and never changed, so earlier versions stay readable.
public sealed class RuleVersionRow
{
    public Guid Id { get; set; }

    public Guid RuleId { get; set; }

    public int Version { get; set; }

    public string Definition { get; set; } = string.Empty;
}

// How proposing checks for one version of a rule went (RUL-04); a later request to propose again replaces it.
public sealed class RuleCompilationRow
{
    public const int MaxMessageLength = 2000;

    public Guid Id { get; set; }

    public Guid RuleId { get; set; }

    public int Version { get; set; }

    public RuleCompileStatus Status { get; set; }

    public string? Message { get; set; }

    // The job that is or was proposing; only that job records what it found.
    public Guid? JobId { get; set; }
}

// A check proposed for a version of a rule, numbered within the rule. CheckId is the check it became once confirmed.
public sealed class RuleCheckProposalRow
{
    public Guid Id { get; set; }

    public Guid RuleId { get; set; }

    public int RuleVersion { get; set; }

    public int Number { get; set; }

    public string CheckName { get; set; } = string.Empty;

    public string Definition { get; set; } = string.Empty;

    public RuleProposalState State { get; set; }

    public Guid? CheckId { get; set; }
}

internal sealed class RuleConfiguration : IEntityTypeConfiguration<RuleRow>
{
    public void Configure(EntityTypeBuilder<RuleRow> row)
    {
        row.ToTable("Rules", table =>
        {
            table.HasCheckConstraint("CK_Rules_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_Rules_ActiveVersion", "\"ActiveVersion\" IS NULL OR \"ActiveVersion\" >= 1");
        });
        row.HasKey(rule => rule.Id);
        row.Property(rule => rule.Id).ValueGeneratedNever();
        row.Property(rule => rule.Slug).HasMaxLength(SchemaNames.MaxLength).IsRequired();

        // Stored as names, so the table reads without the enum and a reordered enum cannot change stored meaning.
        row.Property(rule => rule.State).HasConversion<string>().HasMaxLength(16);

        row.HasOne<SiteRow>().WithMany().HasForeignKey(rule => rule.SiteId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two concurrent creates of one slug when both passed the check.
        row.HasIndex(rule => new { rule.SiteId, rule.Slug }).IsUnique();
    }
}

internal sealed class RuleVersionConfiguration : IEntityTypeConfiguration<RuleVersionRow>
{
    public void Configure(EntityTypeBuilder<RuleVersionRow> row)
    {
        row.ToTable("RuleVersions", table => table.HasCheckConstraint("CK_RuleVersions_Version", "\"Version\" >= 1"));
        row.HasKey(version => version.Id);
        row.Property(version => version.Id).ValueGeneratedNever();
        row.Property(version => version.Definition).IsRequired();

        row.HasOne<RuleRow>().WithMany().HasForeignKey(version => version.RuleId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(version => new { version.RuleId, version.Version }).IsUnique();
    }
}

internal sealed class RuleCompilationConfiguration : IEntityTypeConfiguration<RuleCompilationRow>
{
    public void Configure(EntityTypeBuilder<RuleCompilationRow> row)
    {
        row.ToTable("RuleCompilations", table => table.HasCheckConstraint("CK_RuleCompilations_Version", "\"Version\" >= 1"));
        row.HasKey(compilation => compilation.Id);
        row.Property(compilation => compilation.Id).ValueGeneratedNever();
        row.Property(compilation => compilation.Status).HasConversion<string>().HasMaxLength(16);
        row.Property(compilation => compilation.Message).HasMaxLength(RuleCompilationRow.MaxMessageLength);

        row.HasOne<RuleRow>().WithMany().HasForeignKey(compilation => compilation.RuleId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(compilation => new { compilation.RuleId, compilation.Version }).IsUnique();
    }
}

internal sealed class RuleCheckProposalConfiguration : IEntityTypeConfiguration<RuleCheckProposalRow>
{
    public void Configure(EntityTypeBuilder<RuleCheckProposalRow> row)
    {
        row.ToTable("RuleCheckProposals", table =>
        {
            table.HasCheckConstraint("CK_RuleCheckProposals_RuleVersion", "\"RuleVersion\" >= 1");
            table.HasCheckConstraint("CK_RuleCheckProposals_Number", "\"Number\" >= 1");
        });
        row.HasKey(proposal => proposal.Id);
        row.Property(proposal => proposal.Id).ValueGeneratedNever();
        row.Property(proposal => proposal.CheckName).HasMaxLength(SchemaNames.MaxLength).IsRequired();
        row.Property(proposal => proposal.Definition).IsRequired();
        row.Property(proposal => proposal.State).HasConversion<string>().HasMaxLength(16);

        row.HasOne<RuleRow>().WithMany().HasForeignKey(proposal => proposal.RuleId).OnDelete(DeleteBehavior.Restrict);
        row.HasOne<CheckRow>().WithMany().HasForeignKey(proposal => proposal.CheckId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two compiles of one rule from numbering proposals alike.
        row.HasIndex(proposal => new { proposal.RuleId, proposal.Number }).IsUnique();
        row.HasIndex(proposal => new { proposal.RuleId, proposal.RuleVersion });

        // A check is confirmed from one proposal at most, which the check run reads to find the rule it serves.
        row.HasIndex(proposal => proposal.CheckId).IsUnique();
    }
}
