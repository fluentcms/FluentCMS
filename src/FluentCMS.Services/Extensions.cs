using FluentCMS.Entities.Users;
using FluentCMS.Services.Identity;
using Microsoft.AspNetCore.Identity;
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

        services.AddTransient<FluentCmsUserStore>();
        services.AddTransient<FluentCmsRoleStore>();


        return services;
    }
}
