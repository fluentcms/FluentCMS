using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
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
