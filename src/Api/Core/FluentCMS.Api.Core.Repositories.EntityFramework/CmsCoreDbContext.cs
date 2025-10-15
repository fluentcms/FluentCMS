using File = FluentCMS.Api.Core.Models.File;

namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class CmsCoreDbContext(DbContextOptions<CmsCoreDbContext> options) : IdentityDbContext<
        User,              // TUser
        Role,              // TRole
        Guid,                         // TKey
        IdentityUserClaim<Guid>,      // TUserClaim
        IdentityUserRole<Guid>,       // TUserRole
        IdentityUserLogin<Guid>,      // TUserLogin
        IdentityRoleClaim<Guid>,      // TRoleClaim
        IdentityUserToken<Guid>       // TUserToken
    >(options)

{

    #region DbSets

    // DbSets for all entities
    public DbSet<ApiToken> ApiTokens { get; set; } = default!;
    public DbSet<Policy> Policies { get; set; } = default!;
    public DbSet<File> Files { get; set; } = default!;
    public DbSet<Folder> Folders { get; set; } = default!;
    public DbSet<GlobalSettings> GlobalSettings { get; set; } = default!;
    public DbSet<Layout> Layouts { get; set; } = default!;
    public DbSet<Page> Pages { get; set; } = default!;
    public DbSet<Permission> Permissions { get; set; } = default!;
    public DbSet<Plugin> Plugins { get; set; } = default!;
    public DbSet<PluginDefinition> PluginDefinitions { get; set; } = default!;
    public DbSet<Site> Sites { get; set; } = default!;

    #endregion

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        //// Put Identity into a dedicated schema (optional but recommended)
        //const string schema = "auth";

        //// Rename tables (optional: keeps DB tidy & predictable)
        //builder.Entity<User>().ToTable("Users", schema);
        //builder.Entity<Role>().ToTable("Roles", schema);
        //builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles", schema);
        //builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims", schema);
        //builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins", schema);
        //builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims", schema);
        //builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens", schema);
    }
}

