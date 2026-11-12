using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;

namespace FluentCmsPlugin1;

public sealed class StarterNote
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public string Text { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}

// The plugin's tables, named cms_starter_plugin_*. A change here needs a migration in each set of Migrations/; see the README.
public sealed class StarterModel : IPluginModelContributor
{
    public const string NoteTable = "cms_starter_plugin_note";

    public void Configure(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StarterNote>(note =>
        {
            note.ToTable(NoteTable);
            note.HasKey(item => item.Id);
            note.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            note.Property(item => item.SiteId).HasColumnName("site_id");
            note.Property(item => item.Text).HasColumnName("text").HasMaxLength(500).IsRequired();
            note.Property(item => item.CreatedAt).HasColumnName("created_at");
            note.HasIndex(item => new { item.SiteId, item.CreatedAt }).HasDatabaseName("ix_cms_starter_plugin_note_site_id_created_at");
        });
}
