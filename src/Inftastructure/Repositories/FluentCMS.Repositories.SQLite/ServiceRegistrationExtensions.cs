using FluentCMS.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Repositories.Sqlite;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddSqliteDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDatabaseConfiguration>(sp =>
        {
            return new SqliteDatabaseConfiguration(connectionString);
        });
        return services;
    }
}
