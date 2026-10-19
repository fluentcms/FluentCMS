using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Routing;

internal sealed class RoutingRuleConfiguration : IEntityTypeConfiguration<RoutingRuleRow>
{
    public void Configure(EntityTypeBuilder<RoutingRuleRow> row)
    {
        row.ToTable("RoutingRules", table =>
        {
            table.HasCheckConstraint("CK_RoutingRules_Version", "\"Version\" >= 1");
            table.HasCheckConstraint(
                "CK_RoutingRules_ActiveVersion",
                "\"ActiveVersion\" IS NULL OR (\"ActiveVersion\" >= 1 AND \"ActiveVersion\" <= \"Version\")");
        });
        row.HasKey(rule => rule.Id);
        row.Property(rule => rule.Id).ValueGeneratedNever();
        row.Property(rule => rule.Name).HasMaxLength(SchemaNames.MaxLength).IsRequired();

        row.HasOne<SiteRow>().WithMany().HasForeignKey(rule => rule.SiteId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two concurrent creates of one name when both passed the check.
        row.HasIndex(rule => new { rule.SiteId, rule.Name }).IsUnique();
    }
}

internal sealed class RoutingRuleVersionConfiguration : IEntityTypeConfiguration<RoutingRuleVersionRow>
{
    public void Configure(EntityTypeBuilder<RoutingRuleVersionRow> row)
    {
        row.ToTable("RoutingRuleVersions", table => table.HasCheckConstraint("CK_RoutingRuleVersions_Version", "\"Version\" >= 1"));
        row.HasKey(version => version.Id);
        row.Property(version => version.Id).ValueGeneratedNever();
        row.Property(version => version.Definition).IsRequired();

        row.HasOne<RoutingRuleRow>().WithMany().HasForeignKey(version => version.RoutingRuleId).OnDelete(DeleteBehavior.Restrict);
        row.HasIndex(version => new { version.RoutingRuleId, version.Version }).IsUnique();
    }
}
