using FluentCMS.Repositories.EFCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class EFCoreServiceExtensions
{
    public static IServiceCollection AddEFCoreRepositories(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(MappingProfile));

        // Register repositories
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IContentRepository, ContentRepository>();
        services.AddScoped<IContentTypeRepository, ContentTypeRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IGlobalSettingsRepository, GlobalSettingsRepository>();
        services.AddScoped<ILayoutRepository, LayoutRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPluginContentRepository, PluginContentRepository>();
        services.AddScoped<IPluginDefinitionRepository, PluginDefinitionRepository>();
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        //services.AddScoped<IUserRepository, UserRepository>();
        //services.AddScoped<IUserRoleRepository, UserRoleRepository>();

        return services;
    }
}
