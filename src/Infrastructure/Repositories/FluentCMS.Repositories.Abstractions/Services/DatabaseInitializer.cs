using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.Abstractions.Services;

/// <summary>
/// Service responsible for initializing databases and running data seeders based on area configurations.
/// </summary>
public class DatabaseInitializer(IEnumerable<IDataSeeder> dataSeeders, ILogger<DatabaseInitializer>? logger = null) : IDataInitializer
{
    /// <summary>
    /// Initializes all databases by running seeders for areas that have seeding enabled and conditions met.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    public async Task InitializeAll(CancellationToken cancellationToken = default)
    {
        // Get all registered data seeders
        var dataSeedersList = dataSeeders.ToList();

        if (dataSeedersList.Count == 0)
        {
            logger?.LogInformation("No data seeders found");
            return;
        }

        // Group seeders by priority and execute in order
        var prioritizedSeeders = dataSeeders
            .OrderBy(s => s.Priority)
            .ToList();

        foreach (var seeder in prioritizedSeeders)
        {
            try
            {
                // Check if data already exists
                if (await seeder.HasData(cancellationToken))
                {
                    logger?.LogInformation("Data already exists for seeder {SeederType}, skipping", seeder.GetType().Name);
                    continue;
                }

                // Check if seeding should execute based on conditions
                // Note: Conditions are evaluated per area during service registration
                // If the seeder is registered, it means conditions passed

                logger?.LogInformation("Running data seeder {SeederType}", seeder.GetType().Name);
                await seeder.SeedData(cancellationToken);
                logger?.LogInformation("Completed data seeder {SeederType}", seeder.GetType().Name);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error running data seeder {SeederType}", seeder.GetType().Name);
                throw; // Re-throw to fail fast unless configured otherwise
            }
        }
    }
}
