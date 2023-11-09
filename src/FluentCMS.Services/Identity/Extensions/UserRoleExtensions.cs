using uBeac.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class UserRoleExtensions
{
    public static IServiceCollection AddUserRoleService<TUserRoleService, TUserKey, TUser>(this IServiceCollection services)
        where TUserKey : IEquatable<TUserKey>
        where TUser : User<TUserKey>
        where TUserRoleService : class, IUserRoleService<TUserKey, TUser>
    {
        services.AddScoped<IUserRoleService<TUserKey, TUser>, TUserRoleService>();
        return services;
    }

    public static IServiceCollection AddUserRoleService<TUserRoleService, TUser>(this IServiceCollection services)
        where TUser : User
        where TUserRoleService : class, IUserRoleService<TUser>
    {
        services.AddScoped<IUserRoleService<TUser>, TUserRoleService>();
        return services;
    }
}