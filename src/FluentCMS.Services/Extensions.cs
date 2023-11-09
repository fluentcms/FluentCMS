using FluentCMS.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class Extensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<IHostService, HostService>();

        return services;
    }
}
