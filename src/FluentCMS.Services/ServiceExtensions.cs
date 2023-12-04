using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Services.Identity.Stores;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddPolicies();
        services.AddScoped<IAuthorizationProvider, AuthorizationProvider>();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<IHostService, HostService>();
        services.AddScoped<IPluginService, PluginService>();
        services.AddScoped<IPluginDefinitionService, PluginDefinitionService>();
        services.AddScoped<ILayoutService, LayoutService>();
        services.AddScoped(typeof(IContentService<>), typeof(ContentService<>));
        services.AddScoped<IPluginContentService, PluginContentService>();

        services.AddIdentity();

        return services;
    }

    private static IdentityBuilder AddIdentity(this IServiceCollection services)
    {
        var builder = services.AddIdentityCore<User>();

        builder
            .AddUserStore<UserStore>()
            .AddUserManager<UserManager<User>>()
            .AddDefaultTokenProviders();

        return builder;
    }

}
