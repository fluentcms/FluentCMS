namespace FluentCMS.Infrastructure.Configuration.EntityFramework;

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

    public static IServiceCollection AddDbConfiguration(this IServiceCollection services)
    {
        services.AddDatabaseConfigurationRegistry();
        services.AddDatabaseContext<ConfigurationDbContext, IConfigurationDatabaseMarker>();
        services.AddDataSeeder<ConfigurationDataSeeder, IConfigurationDatabaseMarker>();
        services.AddSchemaValidator<ConfigurationSchemaValidator, IConfigurationDatabaseMarker>();
        services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        return services;
    }

    public static ConfigurationManager CreateTemporaryConfigurationManager()
    {
        // This creates ONLY configuration, no DI container
        var tempConfig = new ConfigurationManager();
        tempConfig.SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables();

        return tempConfig;
    }
}
