using FluentCMS.Services.Permissions;

namespace Microsoft.Extensions.DependencyInjection;

public static class PermissionServiceExtensions
{
    public static IServiceCollection AddPermissions(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPermissionManager<>), typeof(PermissionManager<>));
        return services;
    }
}
