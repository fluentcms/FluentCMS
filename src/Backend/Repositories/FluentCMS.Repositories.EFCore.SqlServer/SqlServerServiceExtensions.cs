using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EFCore;
using FluentCMS.Repositories.EFCore.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class SqlServerServiceExtensions
{
    public static IServiceCollection AddSqlServerRepositories(this IServiceCollection services, string connectionStringName)
    {
        services.AddScoped<ISetupRepository, SetupRepository>();

        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>((sp, options) =>
            options.UseSqlServer(sp.GetConnectionString(connectionStringName)));

        return services;
    }

    private static string GetConnectionString(this IServiceProvider provider, string connectionStringName)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var connString = configuration.GetConnectionString(connectionStringName);
        return connString ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' not found.");
    }
}
