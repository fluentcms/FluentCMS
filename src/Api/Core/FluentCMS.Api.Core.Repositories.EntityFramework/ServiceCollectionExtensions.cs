namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntityFrameworkRpositories(this IServiceCollection services)
    {
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPluginDefinitionRepository, PluginDefinitionRepository>();
        services.AddScoped<IPluginRepository, PluginRepository>();

        services.AddDatabaseContext<CmsCoreDbContext, ICoreDatabaseArea>();

        return services;
    }
}

