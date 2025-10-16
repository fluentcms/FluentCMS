namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntityFrameworkRpositories(this IServiceCollection services)
    {
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<ILayoutRepository, LayoutRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPluginDefinitionRepository, PluginDefinitionRepository>();
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();

        services.AddDatabaseContext<CmsCoreDbContext, ICoreDatabaseArea>();

        return services;
    }
}

