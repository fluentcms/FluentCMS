using FluentCMS.Repositories.EFCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class MySqlServiceExtensions
{
    public static IServiceCollection AddMySqlRepositories(this IServiceCollection services, string connectionStringName)
    {
        services.AddEFCoreRepositories();

        services.AddDbContext<FluentCmsDbContext>((sp, options) =>
            options.UseMySql(sp.GetConnectionString(connectionStringName), ServerVersion.AutoDetect(connectionStringName)));

        return services;
    }

    private static string GetConnectionString(this IServiceProvider provider, string connectionStringName)
    {
        var configuration = provider.GetRequiredService<IConfiguration>();
        var connString = configuration.GetConnectionString(connectionStringName);
        return connString ?? throw new InvalidOperationException($"Connection string '{connectionStringName}' not found.");
    }
}
