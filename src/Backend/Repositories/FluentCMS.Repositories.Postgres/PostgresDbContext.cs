using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace FluentCMS.Repositories.Postgres;

public class PostgresDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    readonly IApiExecutionContext _apiExecutionContext;
    public DbSet<ApiToken> ApiTokens { get; set; } = default!;
    public DbSet<Content> Contents { get; set; } = default!;
    public DbSet<ContentType> ContentTypes { get; set; } = default!;
    public DbSet<ContentTypeField> ContentTypeFields { get; set; } = default!;
    public DbSet<File> Files { get; set; } = default!;
    public DbSet<Folder> Folders { get; set; } = default!;
    public DbSet<GlobalSettings> GlobalSettings { get; set; } = default!;
    public DbSet<Permission> Permissions { get; set; } = default!;
    public DbSet<PluginContent> PluginContents { get; set; } = default!;
    public DbSet<PluginDefinition> PluginDefinitions { get; set; } = default!;

    public DbSet<Site> Sites { get; set; } = default!;
    public DbSet<Layout> Layouts { get; set; } = default!;
    public DbSet<Page> Pages { get; set; } = default!;
    public DbSet<Block> Blocks { get; set; } = default!;

    public PostgresDbContext(DbContextOptions<PostgresDbContext> options, IApiExecutionContext apiExecutionContext): base(options)
    {
        _apiExecutionContext = apiExecutionContext;
     }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(PostgresDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseExceptionProcessor();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        ChangeTracker.DetectChanges();
        ChangeTracker.AutoDetectChangesEnabled = false;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            var now = DateTime.UtcNow;

            var userName = _apiExecutionContext.Username;
            var entity = entry.Entity;

            switch (entry.State)
            {
                case EntityState.Added or EntityState.Modified:
                {
                    entity.ModifiedBy = userName;

                    entity.ModifiedAt = now;

                    if (entity.CreatedAt != default)
                    {
                        continue;
                    }

                    entity.CreatedAt = now;
                    entity.CreatedBy = userName;
                    break;
                }
            }
        }

        ChangeTracker.AutoDetectChangesEnabled = true;

        return base.SaveChangesAsync(cancellationToken);
    }
}
