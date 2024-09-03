using FluentCMS.Services.Setup;
using FluentCMS.Services.Setup.Handlers;

namespace Microsoft.Extensions.DependencyInjection;

public static class SetupServiceExtensions
{
    public static IServiceCollection AddSetupServices(this IServiceCollection services)
    {
        services.AddScoped<BaseSetupHandler, ApiTokenHandler>();
        services.AddScoped<BaseSetupHandler, SuperAdminHandler>();
        services.AddScoped<BaseSetupHandler, SiteHandler>();
        services.AddScoped<BaseSetupHandler, PluginHandler>();
        services.AddScoped<BaseSetupHandler, LayoutHandler>();
        services.AddScoped<BaseSetupHandler, BlockHandler>();
        services.AddScoped<BaseSetupHandler, PageHandler>();
        services.AddScoped<BaseSetupHandler, GlobalSettingsHandler>();
        services.AddScoped<BaseSetupHandler, ContentTypeHandler>();
        services.AddScoped<BaseSetupHandler, SetInitializedHandler>();

        return services;
    }
}
