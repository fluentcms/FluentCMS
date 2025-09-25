using FluentCMS.DataSeeding.Abstractions;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.DataSeeding.Conditions;

/// <summary>
/// Condition that checks configuration settings to determine if seeding should execute.
/// Compares a configuration value against an expected value using case-insensitive comparison.
/// </summary>
public class ConfigurationCondition(IConfiguration configuration, string configKey, string expectedValue) : ICondition
{
    /// <summary>
    /// Gets the descriptive name of this condition for logging purposes
    /// </summary>
    public string Name => $"Configuration: {configKey} = {expectedValue}";

    /// <summary>
    /// Evaluates whether seeding should execute based on configuration value comparison
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if the configuration value matches the expected value (case-insensitive), false otherwise</returns>
    public Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate inputs
            if (string.IsNullOrEmpty(configKey))
                throw new InvalidOperationException("Configuration key cannot be null or empty");

            // Retrieve the configuration value using the provided key
            var configValue = configuration[configKey];

            // Compare the actual configuration value with the expected value (case-insensitive)
            return Task.FromResult(string.Equals(configValue, expectedValue, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Provide better error context for configuration-related failures
            throw new InvalidOperationException($"Configuration condition evaluation failed for key '{configKey}': {ex.Message}", ex);
        }
    }
}
