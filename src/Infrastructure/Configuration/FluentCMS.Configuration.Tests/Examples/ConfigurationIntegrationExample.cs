using FluentCMS.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Configuration.Tests.Examples;

/// <summary>
/// Example showing how to integrate the improved database configuration provider
/// </summary>
public static class ConfigurationIntegrationExample
{
    /// <summary>
    /// Example of setting up database configuration with seeding
    /// </summary>
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 1. Set up database options
        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite("Data Source=configuration.db")
            .Options;

        // 2. Configure sections to be seeded (optional - can be auto-discovered)
        var sectionsToSeed = new List<string>
        {
            "Logging",
            "ApiSettings", 
            "FeatureFlags",
            "ExternalServices"
        };

        // 3. Add the seeding hosted service
        services.AddDatabaseConfigurationSeeding(
            dbOptions,
            seedConfiguration: configuration,
            dynamicSections: sectionsToSeed
        );

        // 4. Register the provider for runtime access
        services.AddDatabaseConfigurationServices(configuration);
    }

    /// <summary>
    /// Example of setting up the configuration builder
    /// </summary>
    public static IConfigurationBuilder ConfigureConfiguration(IConfigurationBuilder builder)
    {
        // First, add standard configuration sources
        builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
        builder.AddEnvironmentVariables();

        // Then add database configuration (with 5 minute reload interval)
        var dbOptions = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseSqlite("Data Source=configuration.db")
            .Options;

        builder.AddDatabaseConfiguration(dbOptions, TimeSpan.FromMinutes(5));

        return builder;
    }

    /// <summary>
    /// Example usage in a service
    /// </summary>
    public class ExampleService
    {
        private readonly DatabaseConfigurationProvider _configProvider;
        private readonly IConfiguration _configuration;

        public ExampleService(DatabaseConfigurationProvider configProvider, IConfiguration configuration)
        {
            _configProvider = configProvider;
            _configuration = configuration;
        }

        public async Task<T?> GetDynamicConfigurationAsync<T>(string section) where T : class
        {
            // Get configuration from database with caching
            return await _configProvider.GetConfigurationAsync<T>(section);
        }

        public async Task UpdateConfigurationAsync<T>(string section, T configuration) where T : class
        {
            // Update configuration in database and trigger reload
            await _configProvider.UpdateConfigurationAsync(section, configuration);
        }

        public T GetStaticConfiguration<T>(string section) where T : class, new()
        {
            // Get configuration from standard sources (appsettings.json, env vars, etc.)
            return _configuration.GetSection(section).Get<T>() ?? new T();
        }
    }
}
