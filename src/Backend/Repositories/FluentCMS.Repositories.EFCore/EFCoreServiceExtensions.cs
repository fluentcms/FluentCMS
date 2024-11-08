using FluentCMS.Repositories.EFCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class EFCoreServiceExtensions
{
    public static IServiceCollection AddSqlServerDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services;
    }

    public static IServiceCollection AddMySqlDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        return services;
    }

    public static IServiceCollection AddSqliteDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>(options =>
            options.UseSqlite(connectionString));

        return services;
    }

    public static IServiceCollection AddPostgresDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }

    private static IServiceCollection AddEFCoreRepositories(this IServiceCollection services)
    {
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
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();

        return services;
    }
}
