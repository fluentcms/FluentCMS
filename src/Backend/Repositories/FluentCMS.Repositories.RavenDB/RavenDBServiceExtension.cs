using System;
using FluentCMS.Repositories.RavenDB;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class RavenDBServiceExtension
{
    public static IServiceCollection AddRavenDbRepositories(this IServiceCollection services, string connectionString)
    {
        // Register MongoDB context and options
        services.AddSingleton(provider => CreateRavenDBOptions(provider, connectionString));
        services.AddSingleton<IRavenDBContext, RavenDBContext>();

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
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();

        return services;
    }

    private static RavenDBOptions<RavenDBContext> CreateRavenDBOptions(IServiceProvider provider, string connectionString)
    {
        var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
        var connString = configuration.GetConnectionString(connectionString);
        return connString is not null
            ? new RavenDBOptions<RavenDBContext>(connString)
            : throw new InvalidOperationException($"Connection string '{connectionString}' not found.");
    }
}
