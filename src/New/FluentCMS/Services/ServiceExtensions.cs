using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Services.Identity.Stores;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IHostService, HostService>();
        services.AddScoped(typeof(IContentService<>), typeof(ContentService<>));

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
