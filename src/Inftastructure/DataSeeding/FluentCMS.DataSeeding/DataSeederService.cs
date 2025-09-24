using FluentCMS.DataSeeding.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.DataSeeding;

/// <summary>
/// Service responsible for orchestrating the data seeding process.
/// Executes data seeders in priority order while respecting configured conditions.
/// </summary>
/// <param name="dataSeeders">Collection of data seeders to execute</param>
/// <param name="logger">Logger for tracking seeding operations</param>
/// <param name="dataSeederOptions">Configuration options for the seeding process</param>
internal class DataSeederService(IEnumerable<IDataSeeder> dataSeeders, ILogger<DataSeederService> logger, IOptions<DataSeederOptions> dataSeederOptions)
{
    // Cache ordered seeders by priority to avoid repeated ordering operations
    private readonly IDataSeeder[] orderedDataSeeders = dataSeeders.OrderBy(s => s.Priority).ToArray();

    /// <summary>
    /// Ensures that seed data is created if all conditions are met and data doesn't already exist.
    /// Validates all configured conditions before proceeding with seeding operations.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous seeding operation</returns>
    public async Task EnsureSeedData(CancellationToken cancellationToken)
    {
        // Check all conditions in parallel - ALL must be met to proceed with seeding
        if (dataSeederOptions.Value.Conditions.Count > 0)
        {
            var conditionResults = await Task.WhenAll(
                dataSeederOptions.Value.Conditions.Select(async condition =>
                {
                    var result = await condition.ShouldExecute(cancellationToken);
                    if (!result)
                    {
                        logger.LogInformation("Seeding condition '{Name}' not met. Skipping seeding process.", condition.Name);
                    }
                    return result;
                }));

            // If any condition failed, skip seeding
            if (conditionResults.Any(result => !result))
                return;
        }

        // Execute each seeder in priority order using cached ordered collection
        foreach (var seeder in orderedDataSeeders)
        {
            // Skip seeding if data already exists (idempotent operation)
            if (await seeder.HasData(cancellationToken))
            {
                logger.LogInformation("Data for seeder '{Name}' already exists. Skipping seeding.", seeder.GetType().Name);
                continue;
            }

            // Proceed with seeding for this seeder
            await SeedData(seeder, cancellationToken);
        }
    }

    /// <summary>
    /// Executes the seeding operation for a specific data seeder with error handling.
    /// Logs the operation progress and handles exceptions based on configuration.
    /// </summary>
    /// <param name="dataSeeder">The data seeder to execute</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous seeding operation</returns>
    private async Task SeedData(IDataSeeder dataSeeder, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Seeding data for seeder '{Name}'.", dataSeeder.GetType().Name);

            // Execute the actual seeding operation
            await dataSeeder.SeedData(cancellationToken);

            logger.LogInformation("Data seeded for seeder '{Name}'.", dataSeeder.GetType().Name);
        }
        catch (Exception ex)
        {
            // Log the error with seeder context
            logger.LogError(ex, "Error occurred while seeding data for seeder '{SeederName}'.", dataSeeder.GetType().Name);

            // Re-throw exception unless configured to ignore errors
            if (!dataSeederOptions.Value.IgnoreExceptions)
                throw;
        }
    }
}
