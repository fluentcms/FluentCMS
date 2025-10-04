using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FluentCMS.Repositories.Abstractions.Configuration;
// NOTE: In a real application, these would be separate NuGet packages:
using FluentCMS.Repositories.Sqlite;
using FluentCMS.Repositories.SqlServer;

namespace FluentCMS.Repositories.Abstractions.Examples;

/// <summary>
/// Demonstration of how to use the new AddDatabaseManager functionality.
/// This replaces the need for per-library service collection extensions.
/// </summary>
public static class AddDatabaseManagerDemo
{
    /// <summary>
    /// Example registration showing how different libraries can be configured
    /// to use different database providers while maintaining a centralized configuration approach.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    /// <param name="environment">The host environment for conditional seeding</param>
    public static void ConfigureDatabaseManager(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddDatabaseManager(options =>
        {
            // Default database configuration - applies to any TArea not explicitly configured
            // NOTE: Requires FluentCMS.Repositories.Sqlite package
            options.Default()
                .UseSqlite("Data Source=default.db")
                .EnableDataSeeding(seedingOptions =>
                {
                    seedingOptions.IgnoreExceptions = false; // Fail fast on errors
                    seedingOptions.Conditions.Add(new EnvironmentCondition(
                        environment,
                        e => e.IsDevelopment()));
                });

            // Specific database for Todo library
            // NOTE: Requires FluentCMS.Repositories.SqlServer package
            options.For<ITodoDatabaseMarker>()
                .UseSqlServer("Server=localhost;Database=TodoDb;Trusted_Connection=True;")
                .EnableDataSeeding(seedingOptions =>
                {
                    seedingOptions.IgnoreExceptions = false; // Fail fast on errors
                    seedingOptions.Conditions.Add(new EnvironmentCondition(
                        environment,
                        e => e.IsDevelopment()));
                });

            // TODO: When advanced options are supported (after package extraction):
            // options.Default().UseSqlite("Data Source=default.db", sqliteOpts =>
            //     sqliteOpts.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery));
            //
            // options.For<ITodoDatabaseMarker>().UseSqlServer(
            //     "Server=localhost;Database=TodoDb;Trusted_Connection=True;",
            //     sqlOpts => sqlOpts.EnableRetryOnFailure(maxRetryCount: 5));

            // Example for another library that might use MySQL (when MySQL provider is added)
            // NOTE: Would require FluentCMS.Repositories.MySql package
            // options.For<IIdentityDatabaseMarker>().UseMySql("Server=localhost;Database=IdentityDb;");
        });

        // Note: Libraries no longer need individual service registration methods.
        // All registration (DbContext, Repositories, Seeders) is handled automatically
        // by the DatabaseManager based on the marker interfaces it discovers.
    }
}

/* PREVIOUS APPROACH (No longer needed):
 *
 * Before AddDatabaseManager, each library had to explicitly register its services:
 *
 * public static void AddTodoServices(IServiceCollection services)
 * {
 *     services.AddDataContextForArea<ITodoDatabaseMarker, TodoDbContext>(options =>
 *         options.ConfigureWarnings(warnings => warnings.Throw()));
 *
 *     services.AddDataSeeder<TodoDataSeeder, ITodoDatabaseMarker>();
 *     services.AddScoped<ITodoRepository, TodoRepository>();
 *     services.AddScoped<ITodoAnotherService, TodoAnotherService>();
 *     services.AddScoped<ITodoService, TodoService>();
 * }
 *
 * And in Startup/Program.cs, you'd call these individually:
 * services.AddTodoServices();
 * services.AddIdentityServices();
 * services.AddCrmServices();
 * // etc.
 *
 * NEW APPROACH WITH AddDatabaseManager:
 *
 * Now you have a single, centralized configuration that automatically:
 * 1. Discovers all TArea : IDatabaseArea interfaces
 * 2. Registers appropriate DbContexts based on your configuration
 * 3. Registers repositories and seeders automatically
 * 4. Applies database provider settings per area
 *
 */
