namespace FluentCMS.Repositories.EFCore;

public class FluentCmsDbContext(DbContextOptions<FluentCmsDbContext> options) : DbContext(options)
{

    #region DbSets

    public DbSet<ApiToken> ApiTokens { get; set; } = default!;
    public DbSet<Block> Blocks { get; set; } = default!;
    public DbSet<Content> Contents { get; set; } = default!;
    public DbSet<ContentType> ContentTypes { get; set; } = default!;
    public DbSet<ContentTypeField> ContentTypeFields { get; set; } = default!;
    public DbSet<File> Files { get; set; } = default!;
    public DbSet<Folder> Folders { get; set; } = default!;
    public DbSet<GlobalSettings> GlobalSettings { get; set; } = default!;
    public DbSet<Layout> Layouts { get; set; } = default!;
    public DbSet<Page> Pages { get; set; } = default!;
    public DbSet<Permission> Permissions { get; set; } = default!;
    public DbSet<PluginContent> PluginContents { get; set; } = default!;
    public DbSet<PluginDefinition> PluginDefinitions { get; set; } = default!;
    public DbSet<Plugin> Plugins { get; set; } = default!;
    public DbSet<Role> Roles { get; set; } = default!;
    public DbSet<Settings> Settings { get; set; } = default!;
    public DbSet<SettingsValue> SettingsValues { get; set; } = default!;
    public DbSet<Site> Sites { get; set; } = default!;
    public DbSet<User> Users { get; set; } = default!;
    public DbSet<UserRole> UserRoles { get; set; } = default!;

    #endregion

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
