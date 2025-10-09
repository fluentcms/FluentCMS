namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

/// <summary>
/// DbContext for storing configuration data
/// </summary>
public class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : DbContext(options)
{
    public DbSet<ConfigurationEntity> Configurations => Set<ConfigurationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ConfigurationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Section)
                .IsRequired()
                .HasMaxLength(1000);

            entity.HasIndex(e => e.Section)
                .IsUnique();

            entity.Property(e => e.Value)
                .IsRequired();

            entity.Property(e => e.Type)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.Version)
                .IsRequired();
        });
    }
}
