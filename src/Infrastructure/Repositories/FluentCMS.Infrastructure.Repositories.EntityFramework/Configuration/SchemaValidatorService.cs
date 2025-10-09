namespace FluentCMS.Infrastructure.Repositories.EntityFramework.Configuration;

/// <summary>
/// Validates the database schema to ensure it matches the expected structure.
/// </summary>
internal interface ISchemaValidatorService
{
    Task Initialize(CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements database initialization by executing schema validators
/// </summary>
internal class SchemaValidatorService(IServiceProvider serviceProvider, DatabaseManagerOptions options, ILogger<SchemaValidatorService> logger) : ISchemaValidatorService
{

    public static readonly string _defaultMarkerTypeName = typeof(IDefaultDatabaseArea).Name;

    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Starting database initialization for all configured databases");
        await RunAllSchemaValidatorsGlobally(cancellationToken);
        logger.LogInformation("Database initialization completed for all databases");
    }

    /// <summary>
    /// Collects all schema validations from all databases and executes them in global priority order
    /// </summary>
    private async Task RunAllSchemaValidatorsGlobally(CancellationToken cancellationToken = default)
    {
        var allValidators = new List<(ISchemaValidator Validator, DatabaseConfiguration Configuration)>();

        // Adding validators for registered markers if conditions are valid
        foreach (var markerType in options.GetRegisteredMarkers())
        {
            var config = options.GetConfigurationForMarker(markerType);
            if (!await IsConditionsValid(config.SchemaValidationOptions, markerType.Name, cancellationToken))
            {
                continue;
            }
            allValidators.AddRange(serviceProvider.GetKeyedServices<ISchemaValidator>(markerType).Select(s => (s, config)));
        }

        // Adding validators for default configuration if conditions are valid
        var defaultConfig = options.GetDefaultConfiguration();
        if (await IsConditionsValid(defaultConfig.SchemaValidationOptions, _defaultMarkerTypeName, cancellationToken))
        {
            // Some validator might not be registered with a marker, so also include default ones
            var defaultValidators = serviceProvider.GetKeyedServices<ISchemaValidator>(typeof(IDefaultDatabaseArea));
            var validValidators = defaultValidators.Where(s => !allValidators.Any(existing => existing.Validator.GetType() == s.GetType()));
            allValidators.AddRange(validValidators.Select(s => (s, defaultConfig)));
        }

        // Sort all validators globally by priority
        var sortedValidators = allValidators.OrderBy(s => s.Validator.Priority).ToList();

        if (sortedValidators.Count == 0)
        {
            logger.LogDebug("No schema validators registered across all databases");
            return;
        }

        logger.LogInformation("Executing {Count} schema validator(s) globally in priority order", sortedValidators.Count);

        // Execute validators in global priority order
        foreach (var (validator, configuration) in sortedValidators)
        {
            var validatorName = validator.GetType().Name;
            var markerName = configuration.MarkerType?.Name ?? _defaultMarkerTypeName;
            try
            {
                logger.LogDebug("Checking if schema is valid for {ValidatorName} in {markerName} (Priority: {Priority})", validatorName, markerName, validator.Priority);

                if (!await validator.ValidateSchema(cancellationToken))
                {
                    logger.LogInformation("Schema is invalid, validating using {ValidatorName} for {markerName}", validatorName, markerName);
                    await validator.CreateSchema(cancellationToken);
                    logger.LogInformation("Schema validated successfully using {ValidatorName} for {markerName}", validatorName, markerName);
                }
                else
                {
                    logger.LogDebug("Schema is valid, skipping {ValidatorName} for {markerName}", validatorName, markerName);
                }
            }
            catch (Exception ex) when (configuration.SchemaValidationOptions!.IgnoreExceptions)
            {
                logger.LogError(ex, "Schema validation failed for {ValidatorName} in {markerName}, but continuing due to IgnoreExceptions setting", validatorName, markerName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Schema validation failed for {ValidatorName} in {markerName}", validatorName, markerName);
                throw;
            }
        }

        logger.LogInformation("All schema validators executed successfully");
    }


    private async Task<bool> IsConditionsValid(SchemaValidationOptions? options, string markerType, CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            logger.LogInformation("SchemaValidationOptions for {markerType} is null, schema validation will be skipped", markerType);
            return false;
        }

        // If there is not any condition registered , skip
        if (options.Conditions.Count == 0)
        {
            logger.LogWarning("No conditions registered for schema validation in {markerType}, skipping", markerType);
            return false;
        }

        var conditionResults = await Task.WhenAll(
                options.Conditions.Select(async condition =>
                {
                    var result = await condition.ShouldExecute(cancellationToken);
                    if (!result)
                    {
                        logger.LogInformation("Schema validation in {markerType}, condition '{Name}' not met. Skipping schema validation process.", markerType, condition.Name);
                    }
                    return result;
                }));

        // If any condition failed, skip schema validation
        if (conditionResults.Any(result => !result))
        {
            logger.LogInformation("Schema validation skipped for {markerType} due to unsatisfied conditions", markerType);
            return false;
        }

        return true;
    }

}
