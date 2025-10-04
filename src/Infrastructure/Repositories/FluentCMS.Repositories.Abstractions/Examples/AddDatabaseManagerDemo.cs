using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace FluentCMS.Repositories.Abstractions.Examples;

/// <summary>
/// Demonstration of how to use the new AddDatabaseManager functionality.
/// This replaces the need for per-library service collection extensions.
/// </summary>
public static class AddDatabaseManagerDemo
{
    /// <summary>
    /// Example of how to use actual database providers (requires separate packages)
    /// </summary>
    public static void ConfigureWithRealProviders(IServiceCollection services, IHostEnvironment environment)
    {
        // Register areas first
        services.AddDataContextForArea<ITodoDatabaseMarker, TodoDbContext>();

        // Configure with real database providers
        services.AddDatabaseManager(options =>
        {
            // Default database with SQLite (requires FluentCMS.Repositories.Sqlite package)
            options.Default()
                .UseSqlite("Data Source=default.db", sqliteOptions =>
                {
                    // SQLite-specific options would be configured here
                })
                .EnableDataSeeding(seedingOptions =>
                {
                    seedingOptions.IgnoreExceptions = false;
                    seedingOptions.Conditions.Add(new EnvironmentCondition(
                        environment,
                        e => e.IsDevelopment()));
                });

            // Todo area with SQL Server (requires FluentCMS.Repositories.SqlServer package)
            options.For<ITodoDatabaseMarker>()
                .UseSqlServer("Server=localhost;Database=TodoDb;Trusted_Connection=True;", sqlServerOptions =>
                {
                    // SQL Server-specific options would be configured here
                })
                .EnableDataSeeding(seedingOptions =>
                {
                    seedingOptions.IgnoreExceptions = false;
                    seedingOptions.Conditions.Add(new EnvironmentCondition(
                        environment,
                        e => e.IsDevelopment()));
                });
        });
    }
}
