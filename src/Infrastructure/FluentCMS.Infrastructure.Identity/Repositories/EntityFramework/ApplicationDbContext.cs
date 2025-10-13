namespace FluentCMS.Infrastructure.Identity.Repositories.EntityFramework;

internal class ApplicationDbContext<TUser, TRole>(DbContextOptions<ApplicationDbContext<TUser, TRole>> options) :
    IdentityDbContext<TUser, TRole, Guid, UserClaim, UserRole, UserLogin, RoleClaim, UserToken>(options)
    where TUser : UserBase where TRole : RoleBase
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Change Identity table names
        builder.Entity<TUser>(entity =>
        {
            entity.ToTable("Users");
        });

        builder.Entity<TRole>(entity =>
        {
            entity.ToTable("Roles");
        });

        builder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
        });

        builder.Entity<UserClaim>(entity =>
        {
            entity.ToTable("UserClaims");
        });

        builder.Entity<UserLogin>(entity =>
        {
            entity.ToTable("UserLogins");
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey, e.UserId });
        });

        builder.Entity<RoleClaim>(entity =>
        {
            entity.ToTable("RoleClaims");
        });

        builder.Entity<UserToken>(entity =>
        {
            entity.ToTable("UserTokens");
        });

    }
}
