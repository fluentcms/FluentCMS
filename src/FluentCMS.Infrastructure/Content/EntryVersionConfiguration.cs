using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluentCMS.Infrastructure.Content;

internal sealed class EntryVersionConfiguration : IEntityTypeConfiguration<EntryVersionRow>
{
    // Created by the Entries migration on each provider. A migration that rebuilds the table on SQLite must create it again.
    public const string NoUpdateTrigger = "TR_EntryVersions_NoUpdate";

    public void Configure(EntityTypeBuilder<EntryVersionRow> row)
    {
        row.ToTable("EntryVersions", table =>
        {
            table.HasCheckConstraint("CK_EntryVersions_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_EntryVersions_ContentTypeVersion", "\"ContentTypeVersion\" >= 1");

            // Declared so EF Core never writes this table with SQL a trigger rules out.
            table.HasTrigger(NoUpdateTrigger);
        });
        row.HasKey(version => version.Id);
        row.Property(version => version.Id).ValueGeneratedNever();
        row.Property(version => version.Fields).IsRequired();

        row.HasOne<EntryRow>().WithMany().HasForeignKey(version => version.EntryId).OnDelete(DeleteBehavior.Restrict);

        // Also what stops two concurrent writes from storing the same version twice (CNT-02).
        row.HasIndex(version => new { version.EntryId, version.Version }).IsUnique();
    }
}
