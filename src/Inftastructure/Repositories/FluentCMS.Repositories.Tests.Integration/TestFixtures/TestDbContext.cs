using FluentCMS.Repositories.Tests.Integration.TestEntities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repositories.Tests.Integration.TestFixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<TestUser> TestUsers => Set<TestUser>();
    public DbSet<TestProduct> TestProducts => Set<TestProduct>();
    public DbSet<TestCategory> TestCategories => Set<TestCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure TestUser entity
        modelBuilder.Entity<TestUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Age).IsRequired();
        });

        // Configure TestProduct entity
        modelBuilder.Entity<TestProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Price).HasPrecision(18, 2);
            entity.Property(e => e.CategoryId).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
        });

        // Configure TestCategory entity
        modelBuilder.Entity<TestCategory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });
    }
}
