using FluentCMS.Repositories.EntityFramework.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Configuration.EntityFramework;

/// <summary>
/// Extension methods for adding database configuration
/// </summary>
public static class DatabaseConfigurationExtensions
{
    /// <summary>
    /// Adds database configuration provider to the configuration builder.
    /// Automatically discovers all sections registered via AddDatabaseOptions.
    /// </summary>
    /// <param name="builder">Configuration builder</param>
    /// <param name="dbOptions">Database context options</param>
    /// <param name="reloadInterval">Optional reload interval for automatic updates</param>
    public static IConfigurationBuilder AddDatabaseConfiguration(this IConfigurationBuilder builder, DbContextOptions<ConfigurationDbContext> dbOptions, TimeSpan? reloadInterval = null)
    {
        return builder.Add(new DatabaseConfigurationSource
        {
            DbOptions = dbOptions,
            ReloadInterval = reloadInterval ?? TimeSpan.Zero
        });
    }

    public static IServiceCollection AddDbConfigurationServices(this IServiceCollection services)
    {
        services.AddDatabaseContext<ConfigurationDbContext, IConfigurationDatabaseMarker>();
        services.AddDataSeeder<ConfigurationDataSeeder, IConfigurationDatabaseMarker>();
        services.AddSchemaValidator<ConfigurationSchemaValidator, IConfigurationDatabaseMarker>();
        services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        return services;
    }
}
