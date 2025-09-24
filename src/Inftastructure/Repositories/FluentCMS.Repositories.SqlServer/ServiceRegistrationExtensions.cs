using FluentCMS.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.SqlServer;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddSqlServerDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDatabaseConfiguration>(sp =>
        {
            return new SqlServerDatabaseConfiguration(connectionString);
        });
        return services;
    }
}
