using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Providers.Repositories.EntityFramework;

/// <summary>
/// Entity Framework database context for the provider system.
/// </summary>
public class ProviderDbContext(DbContextOptions<ProviderDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Provider instances stored in the database.
    /// </summary>
    public DbSet<Provider> Providers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Provider entity
        modelBuilder.Entity<Provider>(entity =>
        {
            // Primary key
            entity.HasKey(p => p.Id);

            // Required properties with length constraints
            entity.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(p => p.DisplayName)
                .IsRequired()
                .HasMaxLength(400);

            entity.Property(p => p.Area)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(p => p.ModuleType)
                .IsRequired()
                .HasMaxLength(200);

            // Optional properties
            entity.Property(p => p.Options)
                .HasMaxLength(4000)
                .IsUnicode(true); // Support JSON with Unicode characters

            entity.Property(p => p.IsActive)
                .IsRequired()
                .HasDefaultValue(false);
        });
    }
}
