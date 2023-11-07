using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Services;

public static class Extensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<IContentTypeService, ContentTypeService>();

        // plugins
        services.AddTransient<Pluginses.IFileStoragePlugin, Pluginses.FileSystemStoragePlugin>();

        return services;
    }
}
