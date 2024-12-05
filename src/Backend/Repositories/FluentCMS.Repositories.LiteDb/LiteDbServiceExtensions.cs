using FluentCMS.Repositories.LiteDb;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class LiteDbServiceExtensions
{
    public static IServiceCollection AddLiteDbRepositories(this IServiceCollection services, string connectionString)
    {
        // Register LiteDB context and options
        services.AddSingleton(provider => CreateLiteDBOptions(provider, connectionString));
        services.AddSingleton<ILiteDBContext, LiteDBContext>();

        // Register repositories
        services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
        services.AddScoped<IBlockRepository, BlockRepository>();
        services.AddScoped<IContentRepository, ContentRepository>();
        services.AddScoped<IContentTypeRepository, ContentTypeRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IGlobalSettingsRepository, GlobalSettingsRepository>();
        services.AddScoped<IHttpLogRepository, HttpLogRepository>();
        services.AddScoped<ILayoutRepository, LayoutRepository>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPluginContentRepository, PluginContentRepository>();
        services.AddScoped<IPluginDefinitionRepository, PluginDefinitionRepository>();
        services.AddScoped<IPluginRepository, PluginRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ISetupRepository, SetupRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();

        return services;
    }

    private static LiteDBOptions<LiteDBContext> CreateLiteDBOptions(IServiceProvider provider, string connectionString)
    {
        var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
        var connString = configuration.GetConnectionString(connectionString);
        return connString is not null
            ? new LiteDBOptions<LiteDBContext>(connString)
            : throw new InvalidOperationException($"Connection string '{connectionString}' not found.");
    }
}
