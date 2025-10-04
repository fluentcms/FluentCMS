namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Service that initializes databases with seed data.
/// Executes all registered data seeders across all areas in order of priority,
/// considering per-area seeding conditions.
/// </summary>
public class DataInitializer(IEnumerable<IDataSeeder> dataSeeders, IDatabaseManager databaseManager) : IDataInitializer
{
    /// <summary>
    /// Seeds the database with initial data across all registered seeders.
    /// Seeders are executed in order of priority without considering the TArea.
    /// </summary>
    public async Task InitializeAll(CancellationToken cancellationToken = default)
    {
        // Get all seeders by examining service descriptors
        var allSeeders = dataSeeders.OrderBy(s => s.Priority).ToList();

        // Group seeders by their area type and collect those that should run
        var seedersByArea = allSeeders
            .Select(seeder => new { Seeder = seeder, AreaType = GetAreaTypeFromSeeder(seeder) })
            .Where(x => x.AreaType != null && databaseManager.IsSeedingEnabledForAreaDynamic(x.AreaType))
            .GroupBy(x => x.AreaType!)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Seeder).ToList());

        // For each area with seeders, check conditions and collect seeders to execute
        var seedersToExecute = new List<(int Priority, IDataSeeder Seeder)>();

        foreach (var kvp in seedersByArea)
        {
            var areaType = kvp.Key;
            var seeders = kvp.Value;

            var seedingOptions = databaseManager.GetSeedingOptionsForAreaDynamic(areaType);
            if (seedingOptions == null) continue;

            // Check if all conditions pass for this area
            if (!await ShouldExecuteSeedingForArea(seedingOptions, cancellationToken)) continue;

            // Add seeders for this area
            foreach (var seeder in seeders)
            {
                seedersToExecute.Add((seeder.Priority, seeder));
            }
        }

        // Execute seeders in priority order
        var orderedSeeders = seedersToExecute.OrderBy(x => x.Priority);

        foreach (var (priority, seeder) in orderedSeeders)
        {
            try
            {
                var hasData = await seeder.HasData(cancellationToken);
                if (!hasData)
                {
                    await seeder.SeedData(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                var areaType = GetAreaTypeFromSeeder(seeder);
                var seedingOptions = areaType != null ? databaseManager.GetSeedingOptionsForAreaDynamic(areaType) : null;
                if (!(seedingOptions?.IgnoreExceptions ?? false))
                {
                    throw new InvalidOperationException($"Data seeding failed for seeder {seeder.GetType().Name}", ex);
                }
            }
        }
    }

    private static async Task<bool> ShouldExecuteSeedingForArea(DataSeedingOptions seedingOptions, CancellationToken cancellationToken)
    {
        foreach (var condition in seedingOptions.Conditions)
        {
            if (!await condition.ShouldExecute(cancellationToken))
            {
                return false;
            }
        }
        return true;
    }

    private static Type? GetAreaTypeFromSeeder(IDataSeeder seeder)
    {
        var seederType = seeder.GetType();
        var interfaceType = seederType.GetInterfaces().FirstOrDefault(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDataSeeder<>));
        return interfaceType?.GetGenericArguments()[0];
    }
}

internal static class DatabaseManagerExtensions
{
    public static bool IsSeedingEnabledForAreaDynamic(this IDatabaseManager manager, Type areaType)
    {
        var method = typeof(IDatabaseManager).GetMethod(nameof(IDatabaseManager.IsSeedingEnabledForArea))!.MakeGenericMethod(areaType);
        return (bool)method.Invoke(manager, null)!;
    }

    public static DataSeedingOptions? GetSeedingOptionsForAreaDynamic(this IDatabaseManager manager, Type areaType)
    {
        var method = typeof(IDatabaseManager).GetMethod(nameof(IDatabaseManager.GetSeedingOptionsForArea))!.MakeGenericMethod(areaType);
        return (DataSeedingOptions?)method.Invoke(manager, null);
    }
}
