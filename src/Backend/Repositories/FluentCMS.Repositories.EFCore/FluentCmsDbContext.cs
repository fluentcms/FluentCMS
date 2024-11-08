namespace FluentCMS.Repositories.EFCore;

public class FluentCmsDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        #region Settings and SettingValues

        modelBuilder.Entity<Settings>()
            .HasMany<SettingsValue>()
            .WithOne(sv => sv.Settings)
            .HasForeignKey(sv => sv.SettingsId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SettingsValue>()
            .HasIndex(sv => new { sv.SettingsId, sv.Key })
            .IsUnique();  // Ensure unique keys per Settings instance

        #endregion

        #region GlobalSettings

        // Configure SuperAdmins as a comma-separated string
        modelBuilder.Entity<GlobalSettings>()
            .Property(gs => gs.SuperAdmins)
            .HasConversion(
                v => string.Join(",", v), // Convert IEnumerable<string> to string for storage
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries) // Convert string to IEnumerable<string>
            );

        #endregion

        #region Site Associated Entities

        modelBuilder.Entity<Block>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<File>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<Folder>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<Layout>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<Page>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<Permission>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<Plugin>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<Role>()
            .HasIndex(p => p.SiteId);

        modelBuilder.Entity<UserRole>()
            .HasIndex(p => p.SiteId);

        #endregion

    }
}
