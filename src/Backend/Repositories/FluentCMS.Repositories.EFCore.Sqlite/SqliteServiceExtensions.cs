using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EFCore;
using FluentCMS.Repositories.EFCore.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class SqliteServiceExtensions
{
    public static IServiceCollection AddSqliteRepositories(this IServiceCollection services, string connectionStringName)
    {
        services.AddScoped<ISetupRepository, SetupRepository>();

        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>((sp, options) =>
            options.UseSqlite(sp.GetConnectionString(connectionStringName)));

        return services;
    }

    private static string GetConnectionString(this IServiceProvider provider, string connectionStringName)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var connString = configuration.GetConnectionString(connectionStringName);
        return connString ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' not found.");
    }
}
