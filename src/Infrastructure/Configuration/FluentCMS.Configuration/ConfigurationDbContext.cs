using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Configuration;

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
                .HasMaxLength(450);

            entity.HasIndex(e => e.Section)
                .IsUnique();

            entity.Property(e => e.Value)
                .IsRequired();

            entity.Property(e => e.Type)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .IsRequired();
        });
    }
}
