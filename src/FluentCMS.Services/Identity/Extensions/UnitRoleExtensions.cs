using uBeac.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class UnitRoleExtensions
{
    public static IServiceCollection AddUnitRoleService<TUnitRoleService, TUnitRoleKey, TUnitRole>(this IServiceCollection services)
        where TUnitRoleKey : IEquatable<TUnitRoleKey>
        where TUnitRole : UnitRole<TUnitRoleKey>
        where TUnitRoleService : class, IUnitRoleService<TUnitRoleKey, TUnitRole>
    {
        services.AddScoped<IUnitRoleService<TUnitRoleKey, TUnitRole>, TUnitRoleService>();
        return services;
    }

    public static IServiceCollection AddUnitRoleService<TUnitRoleService, TUnitRole>(this IServiceCollection services)
        where TUnitRole : UnitRole
        where TUnitRoleService : class, IUnitRoleService<TUnitRole>
    {
        services.AddScoped<IUnitRoleService<TUnitRole>, TUnitRoleService>();
        return services;
    }
}