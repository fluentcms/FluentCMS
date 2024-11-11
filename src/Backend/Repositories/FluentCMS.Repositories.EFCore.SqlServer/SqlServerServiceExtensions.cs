using FluentCMS.Repositories.EFCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class SqlServerServiceExtensions
{
    public static IServiceCollection AddSqlServerRepositories(this IServiceCollection services, string connectionString)
    {
        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>((sp, options) =>
            options.UseSqlServer(GetConnectionString(sp, connectionString)));

        return services;
    }

    private static string GetConnectionString(IServiceProvider provider, string connectionString)
    {
        var configuration = provider.GetService<IConfiguration>() ?? throw new InvalidOperationException("IConfiguration is not registered.");
        var connString = configuration.GetConnectionString(connectionString);
        return connString ?? throw new InvalidOperationException($"Connection string '{connectionString}' not found.");
    }
}
