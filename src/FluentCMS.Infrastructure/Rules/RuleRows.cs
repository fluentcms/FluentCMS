using FluentCMS.Core.Rules;
using FluentCMS.Core.Schema;
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
