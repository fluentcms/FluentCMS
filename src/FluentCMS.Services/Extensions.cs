using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Services.Identity.Stores;
using Microsoft.AspNetCore.Identity;

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

        // plugins
        services.AddTransient<Pluginses.IFileStoragePlugin, Pluginses.FileSystemStoragePlugin>();

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

        builder
            .AddRoles<Role>()
            .AddRoleStore<RoleStore>()
            .AddRoleManager<RoleManager<Role>>();

        return builder;
    }

}
