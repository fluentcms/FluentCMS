using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.DataSeeding;

/// <summary>
/// Hosted service responsible for performing database schema validation and data seeding operations
/// during application startup. This service ensures the database is properly initialized before
/// the application begins normal operation.
/// </summary>
/// <param name="serviceProvider">Service provider for creating scoped dependencies</param>
/// <param name="logger">Logger instance for tracking seeding operations and errors</param>
internal sealed class DataSeedingHostedService(IServiceProvider serviceProvider, ILogger<DataSeedingHostedService> logger) : IHostedService
{
    /// <summary>
    /// Starts the data seeding process, including schema validation and seed data insertion.
    /// This method is called automatically when the host starts.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A task representing the asynchronous seeding operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Create a new service scope to resolve dependencies with proper lifetime management
        using var scope = serviceProvider.CreateScope();

        // Resolve required services from the scoped service provider
        var schemaValidatorService = scope.ServiceProvider.GetRequiredService<SchemaValidatorService>();
        var dataSeederService = scope.ServiceProvider.GetRequiredService<DataSeederService>();

        // Resolve options to get timeout configurations
        var schemaValidatorOptions = scope.ServiceProvider.GetService<IOptions<SchemaValidatorOptions>>();
        var dataSeederOptions = scope.ServiceProvider.GetService<IOptions<DataSeederOptions>>();

        // Calculate combined timeout (use the maximum of both timeouts to allow adequate time)
        var schemaTimeout = schemaValidatorOptions?.Value?.Timeout ?? TimeSpan.FromMinutes(5);
        var seedingTimeout = dataSeederOptions?.Value?.Timeout ?? TimeSpan.FromMinutes(5);
        var totalTimeout = TimeSpan.FromTicks(Math.Max(schemaTimeout.Ticks, seedingTimeout.Ticks));

        // Create a combined cancellation token with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(totalTimeout);

        try
        {
            logger.LogInformation("Starting database seeding process with timeout of {Timeout} minutes...", totalTimeout.TotalMinutes);

            // Step 1: Ensure database schema exists and is up to date
            logger.LogInformation("Starting database schema creation if needed...");
            await schemaValidatorService.EnsureSchema(timeoutCts.Token);
            logger.LogInformation("Database schema creation process completed.");

            // Step 2: Seed initial data into the database
            logger.LogInformation("Starting data seeding process...");
            await dataSeederService.EnsureSeedData(timeoutCts.Token);
            logger.LogInformation("Data seeding process completed.");
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Handle timeout-specific cancellation
            logger.LogError("Data seeding process timed out after {Timeout} minutes.", totalTimeout.TotalMinutes);
            throw new TimeoutException($"Data seeding process timed out after {totalTimeout.TotalMinutes} minutes.");
        }
        catch (Exception ex)
        {
            // Log the error and re-throw to prevent application startup with incomplete seeding
            logger.LogError(ex, "An error occurred during the seeding process.");
            throw;
        }
    }

    /// <summary>
    /// Stops the data seeding service. Since seeding is a one-time startup operation,
    /// no cleanup is required during shutdown.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>A completed task</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No cleanup required for data seeding operations
        return Task.CompletedTask;
    }
}
