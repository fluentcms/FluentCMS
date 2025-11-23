using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

internal class CmsCoreDbContext(DbContextOptions<CmsCoreDbContext> options) : DbContext(options), ICmsCoreDatabaseMarker
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Layout> Layouts => Set<Layout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Site>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);

            e.Property(x => x.Description);

            e.Property(x => x.Urls)
                .HasConversion(
                    v => string.Join(";", v),
                    v => v.Split(";", StringSplitOptions.RemoveEmptyEntries).ToList());

            e.Property(x => x.MetaTitle);
            e.Property(x => x.MetaDescription);
            e.Property(x => x.RobotsIndex);
            e.Property(x => x.RobotsFollow);
            e.Property(x => x.RobotsTxt);
            e.Property(x => x.GoogleTagsId);
            e.Property(x => x.OgType);
            e.Property(x => x.Head);

            // Layout relationships (nullable, no cascade)
            e.HasOne<Layout>()
                .WithMany()
                .HasForeignKey(x => x.LayoutId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Layout>()
                .WithMany()
                .HasForeignKey(x => x.EditLayoutId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Layout>()
                .WithMany()
                .HasForeignKey(x => x.DetailLayoutId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Layout>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Head);
            e.Property(x => x.Body);
        });

        modelBuilder.Entity<Page>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Slug); // can be empty for / (home page)
            e.Property(x => x.Order);
            e.Property(x => x.MetaTitle);
            e.Property(x => x.MetaDescription);
            e.Property(x => x.RobotsIndex);
            e.Property(x => x.RobotsFollow);
            e.Property(x => x.OgType);
            e.Property(x => x.Head);
            e.HasOne<Layout>()
                .WithMany()
                .HasForeignKey(x => x.LayoutId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Layout>()
                .WithMany()
                .HasForeignKey(x => x.EditLayoutId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Layout>()
                .WithMany()
                .HasForeignKey(x => x.DetailLayoutId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // Make ParentId nullable
            e.HasOne<Page>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
