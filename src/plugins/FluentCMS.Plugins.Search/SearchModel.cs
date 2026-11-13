using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.Search;

// The searchable text of one document in one locale and one state. Text is folded and split by SearchText, so every
// provider matches the same words. The key is an integer because SQLite's FTS5 table reads it as the rowid.
internal sealed class SearchEntry
{
    public long Id { get; set; }

    public Guid DocumentId { get; set; }

    // Empty for a type that is not localized.
    public string Locale { get; set; } = "";

    public int State { get; set; }

    public string Type { get; set; } = "";

    // Null for a global type.
    public Guid? SiteId { get; set; }

    public string Text { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
}

// The states of core's content rows.
internal static class SearchStates
{
    public const int Draft = 0;
    public const int Published = 1;
}

internal sealed class SearchModel : IPluginModelContributor
{
    public const string EntryTable = "cms_search_entry";

    // The FTS5 table and the GIN index that the SQLite and PostgreSQL migrations add beside the model.
    public const string FtsTable = "cms_search_entry_fts";
    public const string TextIndex = "ix_cms_search_entry_text";

    public const int TypeMaxLength = 64;
    public const int LocaleMaxLength = 16;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchEntry>(entry =>
        {
            entry.ToTable(EntryTable);
            entry.HasKey(item => item.Id);
            entry.Property(item => item.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entry.Property(item => item.DocumentId).HasColumnName("document_id");
            entry.Property(item => item.Locale).HasColumnName("locale").HasMaxLength(LocaleMaxLength).IsRequired();
            entry.Property(item => item.State).HasColumnName("state");
            entry.Property(item => item.Type).HasColumnName("type").HasMaxLength(TypeMaxLength).IsRequired();
            entry.Property(item => item.SiteId).HasColumnName("site_id");
            entry.Property(item => item.Text).HasColumnName("text").IsRequired();
            entry.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            entry.HasIndex(item => new { item.DocumentId, item.Locale, item.State }).IsUnique().HasDatabaseName("ux_cms_search_entry_document");
            entry.HasIndex(item => new { item.Type, item.Locale, item.State, item.SiteId }).HasDatabaseName("ix_cms_search_entry_type");
            entry.HasIndex(item => item.SiteId).HasDatabaseName("ix_cms_search_entry_site");
        });
    }
}
