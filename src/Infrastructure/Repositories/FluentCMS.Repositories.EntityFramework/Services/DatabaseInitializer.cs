using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.EntityFramework.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Repositories.EntityFramework.Services;

/// <summary>
/// Implements database initialization by executing schema validators and data seeders
/// </summary>
internal class DatabaseInitializer(IServiceProvider serviceProvider, DatabaseManagerOptions options, ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAll(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation("Starting database initialization for all configured databases");

        // Phase 1: Collect and execute ALL schema validators globally by priority
        await RunAllSchemaValidatorsGlobally(cancellationToken);

        // Phase 2: Collect and execute ALL data seeders globally by priority
        await RunAllDataSeedersGlobally(cancellationToken);

        logger.LogInformation("Database initialization completed for all databases");
    }

    /// <summary>
    /// Collects all schema validators from all databases and executes them in global priority order
    /// </summary>
    private async Task RunAllSchemaValidatorsGlobally(CancellationToken cancellationToken = default)
    {
        var allValidators = new List<(ISchemaValidator Validator, object DatabaseKey, string DatabaseName, SchemaValidatorOptions Options)>();

        // Collect validators from default database
        var defaultConfig = options.GetDefaultConfiguration();
        if (defaultConfig?.SchemaValidatorOptions != null)
        {
            await CollectSchemaValidators("Default", "Default", defaultConfig.SchemaValidatorOptions, allValidators, cancellationToken);
        }

        // Collect validators from each marker-based database
        foreach (var markerType in options.GetRegisteredMarkers())
        {
            var config = options.GetConfigurationForMarker(markerType);
            if (config.SchemaValidatorOptions != null)
            {
                await CollectSchemaValidators(markerType, markerType.Name, config.SchemaValidatorOptions, allValidators, cancellationToken);
            }
        }

        // Sort all validators globally by priority
        var sortedValidators = allValidators.OrderBy(v => v.Validator.Priority).ToList();

        if (sortedValidators.Count == 0)
        {
            logger.LogDebug("No schema validators registered across all databases");
            return;
        }

        logger.LogInformation("Executing {Count} schema validator(s) globally in priority order", sortedValidators.Count);

        // Execute validators in global priority order
        foreach (var (validator, databaseKey, databaseName, options) in sortedValidators)
        {
            var validatorName = validator.GetType().Name;

            try
            {
                logger.LogDebug("Validating schema using {ValidatorName} for {DatabaseName} (Priority: {Priority})", validatorName, databaseName, validator.Priority);

                if (!await validator.ValidateSchema(cancellationToken))
                {
                    logger.LogInformation("Schema validation failed, creating schema using {ValidatorName} for {DatabaseName}", validatorName, databaseName);
                    await validator.CreateSchema(cancellationToken);
                    logger.LogInformation("Schema created successfully using {ValidatorName} for {DatabaseName}", validatorName, databaseName);
                }
                else
                {
                    logger.LogDebug("Schema validation passed for {ValidatorName} for {DatabaseName}", validatorName, databaseName);
                }
            }
            catch (Exception ex) when (options.IgnoreExceptions)
            {
                logger.LogError(ex, "Schema validation/creation failed for {ValidatorName} in {DatabaseName}, but continuing due to IgnoreExceptions setting", validatorName, databaseName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Schema validation/creation failed for {ValidatorName} in {DatabaseName}", validatorName, databaseName);
                throw;
            }
        }

        logger.LogInformation("All schema validators executed successfully");
    }

    /// <summary>
    /// Collects schema validators from a specific database if conditions are satisfied
    /// </summary>
    private async Task CollectSchemaValidators(object databaseKey, string databaseName, SchemaValidatorOptions options, List<(ISchemaValidator, object, string, SchemaValidatorOptions)> collection, CancellationToken cancellationToken = default)
    {
        // If there is not any condition registered , skip
        if (options.Conditions.Count == 0)
        {
            logger.LogWarning("No conditions registered for schema validation in {DatabaseName}, skipping", databaseName);
            return;
        }

        var conditionResults = await Task.WhenAll(
                options.Conditions.Select(async condition =>
                {
                    var result = await condition.ShouldExecute(cancellationToken);
                    if (!result)
                    {
                        logger.LogInformation("Schema validation in {DatabaseName}, condition '{Name}' not met. Skipping schema creation process.", databaseName, condition.Name);
                    }
                    return result;
                }));

        // If any condition failed, skip schema validation
        if (conditionResults.Any(result => !result))
        {
            logger.LogInformation("Schema validation skipped for {DatabaseName} due to unsatisfied conditions", databaseName);
            return;
        }

        // Get validators registered with this key
        var validators = serviceProvider.GetKeyedServices<ISchemaValidator>(databaseKey).ToList();

        if (validators.Count == 0)
        {
            logger.LogDebug("No schema validators registered for {DatabaseName}", databaseName);
            return;
        }

        logger.LogDebug("Collected {Count} schema validator(s) from {DatabaseName}", validators.Count, databaseName);

        // Add to collection with metadata
        foreach (var validator in validators)
        {
            collection.Add((validator, databaseKey, databaseName, options));
        }
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
