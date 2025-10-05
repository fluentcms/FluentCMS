using FluentCMS.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories;

public interface IDataSeederService
{
    /// <summary>
    /// Seeds the database with initial data if necessary.
    ///  </summary>
    Task Initialize(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements data seeding by executing data seeders
/// </summary>
internal class DataSeederService(IServiceProvider serviceProvider, DatabaseManagerOptions options, ILogger<DataSeederService> logger) : IDataSeederService
{
    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("Starting database schema validation process ...");
        await RunAllDataSeedersGlobally(cancellationToken);
        logger.LogInformation("Database schema validation process completed.");
    }

    /// <summary>
    /// Collects all data seeders from all databases and executes them in global priority order
    /// </summary>
    private async Task RunAllDataSeedersGlobally(CancellationToken cancellationToken = default)
    {
        var allSeeders = new List<(IDataSeeder Seeder, object DatabaseKey, string DatabaseName, DataSeedingOptions Options)>();

        // Collect seeders from default database
        var defaultConfig = options.GetDefaultConfiguration();
        if (defaultConfig?.SeedingOptions != null)
        {
            await CollectDataSeeders("Default", "Default", defaultConfig.SeedingOptions, allSeeders, cancellationToken);
        }

        // Collect seeders from each marker-based database
        foreach (var markerType in options.GetRegisteredMarkers())
        {
            var config = options.GetConfigurationForMarker(markerType);
            if (config.SeedingOptions != null)
            {
                await CollectDataSeeders(markerType, markerType.Name, config.SeedingOptions, allSeeders, cancellationToken);
            }
        }

        // Sort all seeders globally by priority
        var sortedSeeders = allSeeders.OrderBy(s => s.Seeder.Priority).ToList();

        if (sortedSeeders.Count == 0)
        {
            logger.LogDebug("No data seeders registered across all databases");
            return;
        }

        logger.LogInformation("Executing {Count} data seeder(s) globally in priority order", sortedSeeders.Count);

        // Execute seeders in global priority order
        foreach (var (seeder, databaseKey, databaseName, options) in sortedSeeders)
        {
            var seederName = seeder.GetType().Name;

            try
            {
                logger.LogDebug("Checking if data exists for {SeederName} in {DatabaseName} (Priority: {Priority})",
                    seederName, databaseName, seeder.Priority);

                if (!await seeder.HasData(cancellationToken))
                {
                    logger.LogInformation("Data does not exist, seeding using {SeederName} for {DatabaseName}",
                        seederName, databaseName);
                    await seeder.SeedData(cancellationToken);
                    logger.LogInformation("Data seeded successfully using {SeederName} for {DatabaseName}",
                        seederName, databaseName);
                }
                else
                {
                    logger.LogDebug("Data already exists, skipping {SeederName} for {DatabaseName}",
                        seederName, databaseName);
                }
            }
            catch (Exception ex) when (options.IgnoreExceptions)
            {
                logger.LogError(ex, "Data seeding failed for {SeederName} in {DatabaseName}, but continuing due to IgnoreExceptions setting", seederName, databaseName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data seeding failed for {SeederName} in {DatabaseName}", seederName, databaseName);
                throw;
            }
        }

        logger.LogInformation("All data seeders executed successfully");
    }

    /// <summary>
    /// Collects data seeders from a specific database if conditions are satisfied
    /// </summary>
    private async Task CollectDataSeeders(object databaseKey, string databaseName, DataSeedingOptions options, List<(IDataSeeder, object, string, DataSeedingOptions)> collection, CancellationToken cancellationToken = default)
    {
        // If there is not any condition registered , skip
        if (options.Conditions.Count == 0)
        {
            logger.LogWarning("No conditions registered for data seeding in {DatabaseName}, skipping", databaseName);
            return;
        }

        var conditionResults = await Task.WhenAll(
                options.Conditions.Select(async condition =>
                {
                    var result = await condition.ShouldExecute(cancellationToken);
                    if (!result)
                    {
                        logger.LogInformation("Data seeding in {DatabaseName}, condition '{Name}' not met. Skipping data seeding process.", databaseName, condition.Name);
                    }
                    return result;
                }));
        // If any condition failed, skip data seeding
        if (conditionResults.Any(result => !result))
        {
            logger.LogInformation("Data seeding skipped for {DatabaseName} due to unsatisfied conditions", databaseName);
            return;
        }

        // Get seeders registered with this key
        var seeders = serviceProvider.GetKeyedServices<IDataSeeder>(databaseKey).ToList();

        if (seeders.Count == 0)
        {
            logger.LogDebug("No data seeders registered for {DatabaseName}", databaseName);
            return;
        }

        logger.LogDebug("Collected {Count} data seeder(s) from {DatabaseName}", seeders.Count, databaseName);

        // Add to collection with metadata
        foreach (var seeder in seeders)
        {
            collection.Add((seeder, databaseKey, databaseName, options));
        }
    }

}
