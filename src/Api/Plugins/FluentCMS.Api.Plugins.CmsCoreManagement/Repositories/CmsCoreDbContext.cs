using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

internal class CmsCoreDbContext(DbContextOptions<CmsCoreDbContext> options) : DbContext(options), ICmsCoreDatabaseMarker
{
    public DbSet<Site> Sites => Set<Site>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure the Site entity
        modelBuilder.Entity<Site>()
            .HasKey(s => s.Id);
    }
}
