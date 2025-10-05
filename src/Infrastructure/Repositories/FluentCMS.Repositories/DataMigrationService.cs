using FluentCMS.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories;

public interface IDataMigrationService
{
    /// <summary>
    /// Ensures that the database schema is up to date by applying any pending migrations.
    /// </summary>
    Task Initialize(CancellationToken cancellationToken = default);
}

internal class DataMigrationService(IServiceProvider serviceProvider, DatabaseManagerOptions options, ILogger<DataSeederService> logger) : IDataMigrationService
{
    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("Starting database schema validation process ...");
        await RunAllMigrationsGlobally(cancellationToken);
        logger.LogInformation("Database schema validation process completed.");
    }

    /// <summary>
    /// Collects all data migrations from all databases and executes them in global priority order
    /// </summary>
    private async Task RunAllMigrationsGlobally(CancellationToken cancellationToken = default)
    {
        var allMigrations = new List<(IDataMigration Seeder, object DatabaseKey, string DatabaseName, DataMigrationOptions Options)>();

        // Collect seeders from default database
        var defaultConfig = options.GetDefaultConfiguration();
        if (defaultConfig?.MigrationOptions != null)
        {
            await CollectDataMigrations("Default", "Default", defaultConfig.MigrationOptions, allMigrations, cancellationToken);
        }

        // Collect migrations from each marker-based database
        foreach (var markerType in options.GetRegisteredMarkers())
        {
            var config = options.GetConfigurationForMarker(markerType);
            if (config.MigrationOptions != null)
            {
                await CollectDataMigrations(markerType, markerType.Name, config.MigrationOptions, allMigrations, cancellationToken);
            }
        }

        // Sort all migrations globally by priority
        var sortedMigrations = allMigrations.OrderBy(s => s.Seeder.Priority).ToList();

        if (sortedMigrations.Count == 0)
        {
            logger.LogDebug("No data migrations registered across all databases");
            return;
        }

        logger.LogInformation("Executing {Count} data migration(s) globally in priority order", sortedMigrations.Count);

        // Execute migrations in global priority order
        foreach (var (migration, databaseKey, databaseName, options) in sortedMigrations)
        {
            var migrationName = migration.GetType().Name;

            try
            {
                logger.LogInformation("Checking if data exists for {MigrationName} in {DatabaseName} (Priority: {Priority})", migrationName, databaseName, migration.Priority);
                await migration.Migrate(cancellationToken);
                logger.LogInformation("Data migrated successfully using {MigrationName} for {DatabaseName}", migrationName, databaseName);
            }
            catch (Exception ex) when (options.IgnoreExceptions)
            {
                logger.LogError(ex, "Data migration failed for {MigrationName} in {DatabaseName}, but ignoring due to configuration", migrationName, databaseName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data migration failed for {MigrationName} in {DatabaseName}, aborting further migrations", migrationName, databaseName);
                throw;
            }
        }
    }

    /// <summary>
    /// Collects data migrations from a specific database if conditions are satisfied
    /// </summary>
    private async Task CollectDataMigrations(object databaseKey, string databaseName, DataMigrationOptions options, List<(IDataMigration, object, string, DataMigrationOptions)> collection, CancellationToken cancellationToken = default)
    {
        // If there is not any condition registered , skip
        if (options.Conditions.Count == 0)
        {
            logger.LogWarning("No conditions registered for migration in {DatabaseName}, skipping", databaseName);
            return;
        }

        var conditionResults = await Task.WhenAll(
                options.Conditions.Select(async condition =>
                {
                    var result = await condition.ShouldExecute(cancellationToken);
                    if (!result)
                    {
                        logger.LogInformation("Migration in {DatabaseName}, condition '{Name}' not met. Skipping migration process.", databaseName, condition.Name);
                    }
                    return result;
                }));
        // If any condition failed, skip data seeding
        if (conditionResults.Any(result => !result))
        {
            logger.LogInformation("Migration skipped for {DatabaseName} due to unsatisfied conditions", databaseName);
            return;
        }

        // Get migrations registered with this key
        var migrations = serviceProvider.GetKeyedServices<IDataMigration>(databaseKey).ToList();

        if (migrations.Count == 0)
        {
            logger.LogDebug("No data migrations registered for {DatabaseName}", databaseName);
            return;
        }

        logger.LogDebug("Collected {Count} data migration(s) from {DatabaseName}", migrations.Count, databaseName);

        // Add to collection with metadata
        foreach (var migration in migrations)
        {
            collection.Add((migration, databaseKey, databaseName, options));
        }
    }
}
