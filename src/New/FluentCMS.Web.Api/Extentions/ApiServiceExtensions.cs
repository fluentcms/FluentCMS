using FluentCMS;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Decorators;
using FluentCMS.Web.Api;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.Decorate<IAppRepository, AppRepositoryDecorator>();
        services.Decorate<IAppTemplateRepository, AppTemplateRepositoryDecorator>();
        services.Decorate<IContentRepository, ContentRepositoryDecorator>();
        services.Decorate<IContentTypeRepository, ContentTypeRepositoryDecorator>();
        services.Decorate<IGlobalSettingsRepository, GlobalSettingsRepositoryDecorator>();
        services.Decorate<IPageRepository, PageRepositoryDecorator>();
        services.Decorate<IRoleRepository, RoleRepositoryDecorator>();
        services.Decorate<ISiteRepository, SiteRepositoryDecorator>();
        services.Decorate<IUserRepository, UserRepositoryDecorator>();

        services.AddApplicationServices();

        services.AddApiDocumentation();

        services.AddControllers();

        services.AddAuthentication();

        services.AddAuthorization();

        services.AddHttpContextAccessor();

        services.AddScoped<IAuthContext, AuthContext>();

        services.AddScoped<SetupManager>();

        services.AddAutoMapper(typeof(ApiServiceExtensions));

        return services;
    }
}
