using FluentCMS.Entities;
using FluentCMS.Identity;
using FluentCMS.Services;
using Microsoft.AspNetCore.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IUserTokenProvider, JwtUserTokenProvider>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IGlobalSettingsService, GlobalSettingsService>();
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<IContentTypeService, ContentTypeService>();
        services.AddScoped<IAppTemplateService, AppTemplateService>();

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
