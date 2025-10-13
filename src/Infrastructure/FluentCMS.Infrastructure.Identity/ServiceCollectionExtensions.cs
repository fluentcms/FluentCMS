namespace FluentCMS.Infrastructure.Identity;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntityFrameworkIdentity<TUser, TRole>(this IServiceCollection services)
        where TUser : UserBase
        where TRole : RoleBase
    {
        // Repositories registration
        services.AddScoped<IUserRepository<TUser>, UserRepository<TUser, TRole>>();
        services.AddScoped<IRoleRepository<TRole>, RoleRepository<TUser, TRole>>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository<TUser, TRole>>();

        services.AddScoped<IRoleService<TUser, TRole>, RoleService<TUser, TRole>>();

        services.AddDatabaseContext<ApplicationDbContext<TUser, TRole>, IIdentityDatabaseMarker>();

        services.AddIdentity<TUser, TRole>()
            .AddEntityFrameworkStores<ApplicationDbContext<TUser, TRole>>()
            .AddDefaultTokenProviders();

        return services;
    }

}
