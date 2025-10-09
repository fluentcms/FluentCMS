namespace FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;

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
    public static readonly string _defaultMarkerTypeName = typeof(IDefaultDatabaseArea).Name;

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
        var allSeeders = new List<(IDataSeeder Seeder, DatabaseConfiguration Configuration)>();

        // Adding seeders for registered markers if conditions are valid
        foreach (var markerType in options.GetRegisteredMarkers())
        {
            var config = options.GetConfigurationForMarker(markerType);
            if (!await IsConditionsValid(config.SeedingOptions, markerType.Name, cancellationToken))
            {
                continue;
            }
            allSeeders.AddRange(serviceProvider.GetKeyedServices<IDataSeeder>(markerType).Select(s => (s, config)));
        }

        // Adding seeders for default configuration if conditions are valid
        var defaultConfig = options.GetDefaultConfiguration();
        if (await IsConditionsValid(defaultConfig.SeedingOptions, _defaultMarkerTypeName, cancellationToken))
        {
            // Some seeder might not be registered with a marker, so also include default ones
            var defaultSeeders = serviceProvider.GetKeyedServices<IDataSeeder>(typeof(IDefaultDatabaseArea));
            var validSeeders = defaultSeeders.Where(s => !allSeeders.Any(existing => existing.Seeder.GetType() == s.GetType()));
            allSeeders.AddRange(validSeeders.Select(s => (s, defaultConfig)));
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
        foreach (var (seeder, configuration) in sortedSeeders)
        {
            var seederName = seeder.GetType().Name;
            var markerName = configuration.MarkerType?.Name ?? _defaultMarkerTypeName;
            try
            {
                logger.LogDebug("Checking if data exists for {SeederName} in {markerName} (Priority: {Priority})", seederName, markerName, seeder.Priority);

                if (await seeder.ShouldSeed(cancellationToken))
                {
                    logger.LogInformation("Data does not exist, seeding using {SeederName} for {markerName}", seederName, markerName);
                    await seeder.SeedData(cancellationToken);
                    logger.LogInformation("Data seeded successfully using {SeederName} for {markerName}", seederName, markerName);
                }
                else
                {
                    logger.LogDebug("Data already exists, skipping {SeederName} for {markerName}", seederName, markerName);
                }
            }
            catch (Exception ex) when (configuration.SeedingOptions!.IgnoreExceptions)
            {
                logger.LogError(ex, "Data seeding failed for {SeederName} in {markerName}, but continuing due to IgnoreExceptions setting", seederName, markerName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Data seeding failed for {SeederName} in {markerName}", seederName, markerName);
                throw;
            }
        }

        logger.LogInformation("All data seeders executed successfully");
    }

    private async Task<bool> IsConditionsValid(DataSeedingOptions? options, string markerType, CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            logger.LogInformation("DataSeedingOptions for {markerType} is null, data seeding will be skipped", markerType);
            return false;
        }

        // If there is not any condition registered , skip
        if (options.Conditions.Count == 0)
        {
            logger.LogWarning("No conditions registered for data seeding in {markerType}, skipping", markerType);
            return false;
        }

        var conditionResults = await Task.WhenAll(
                options.Conditions.Select(async condition =>
                {
                    var result = await condition.ShouldExecute(cancellationToken);
                    if (!result)
                    {
                        logger.LogInformation("Data seeding in {markerType}, condition '{Name}' not met. Skipping data seeding process.", markerType, condition.Name);
                    }
                    return result;
                }));

        // If any condition failed, skip data seeding
        if (conditionResults.Any(result => !result))
        {
            logger.LogInformation("Data seeding skipped for {markerType} due to unsatisfied conditions", markerType);
            return false;
        }

        return true;
    }

}
