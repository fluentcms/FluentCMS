namespace Microsoft.Extensions.DependencyInjection;

public static class PermissionServiceExtensions
{
    public static IServiceCollection AddPermissions(this IServiceCollection services)
    {
        services.AddScoped<IPermissionManager, PermissionManager>();
        return services;
    }
}
