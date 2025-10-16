namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

internal class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) :
    IdentityDbContext<
        User,              // TUser
        Role,              // TRole
        Guid,              // TKey
        UserClaim,         // TUserClaim
        UserRole,          // TUserRole
        UserLogin,         // TUserLogin
        RoleClaim,         // TRoleClaim
        UserToken          // TUserToken
    >(options)

{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}

