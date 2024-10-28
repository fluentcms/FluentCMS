using FluentCMS.Repositories.Caching;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceExtensions
{
    public static IServiceCollection AddCachedRepositories(this IServiceCollection services)
    {

        // Register repositories
        services.Decorate<IApiTokenRepository, ApiTokenRepository>();
        services.Decorate<IBlockRepository, BlockRepository>();
        services.Decorate<IFolderRepository, FolderRepository>();
        services.Decorate<IGlobalSettingsRepository, GlobalSettingsRepository>();
        services.Decorate<ILayoutRepository, LayoutRepository>();
        services.Decorate<IPageRepository, PageRepository>();
        services.Decorate<IPermissionRepository, PermissionRepository>();
        services.Decorate<IPluginDefinitionRepository, PluginDefinitionRepository>();
        services.Decorate<IPluginRepository, PluginRepository>();
        services.Decorate<IRoleRepository, RoleRepository>();
        services.Decorate<ISettingsRepository, SettingsRepository>();
        services.Decorate<ISiteRepository, SiteRepository>();
        services.Decorate<IUserRoleRepository, UserRoleRepository>();

        return services;
    }
}
