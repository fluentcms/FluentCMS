using FluentCMS.Repositories.EFCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class EFCoreServiceExtensions
{
    //public static IServiceCollection AddMySqlRepositories(this IServiceCollection services, string connectionString)
    //{
    //    services.AddEFCoreRepositories();

    //    services.AddDbContext<FluentCmsDbContext>((sp, options) =>
    //        options.UseMySql(GetConnectionString(sp, connectionString), ServerVersion.AutoDetect(connectionString)));

    //    return services;
    //}

    //public static IServiceCollection AddSqliteRepositories(this IServiceCollection services, string connectionString)
    //{
    //    services.AddEFCoreRepositories();

    //    services.AddDbContext<FluentCmsDbContext>((sp, options) =>
    //        options.UseSqlite("Data Source=fluentcms.db"));

    //    return services;
    //}

    //public static IServiceCollection AddPostgresRepositories(this IServiceCollection services, string connectionString)
    //{
    //    services.AddEFCoreRepositories();

    //    services.AddDbContext<FluentCmsDbContext>((sp, options) =>
    //        options.UseNpgsql(GetConnectionString(sp, connectionString)));

    //    return services;
    //}

    //private static string GetConnectionString(IServiceProvider provider, string connectionString)
    //{
    //    var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
    //    var connString = configuration.GetConnectionString(connectionString);
    //    return connString ?? throw new InvalidOperationException($"Connection string '{connectionString}' not found.");
    //}

    public static IServiceCollection AddEFCoreRepositories(this IServiceCollection services)
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
