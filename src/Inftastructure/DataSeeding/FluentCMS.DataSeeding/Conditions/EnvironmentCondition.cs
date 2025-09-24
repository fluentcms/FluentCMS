using FluentCMS.DataSeeding.Abstractions;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.DataSeeding.Conditions;

/// <summary>
/// Condition that checks the current hosting environment against a custom predicate.
/// This allows for flexible environment-based seeding logic, such as seeding specific
/// data only in Development, Staging, or Production environments.
/// </summary>
/// <param name="environment">The current hosting environment instance</param>
/// <param name="predicate">Custom function to evaluate the environment condition</param>
/// <example>
/// // Example usage: Only seed in Development environment
/// new EnvironmentCondition(env, e => e.IsDevelopment())
/// 
/// // Example usage: Seed in Development or Staging
/// new EnvironmentCondition(env, e => e.IsDevelopment() || e.IsStaging())
/// </example>
public class EnvironmentCondition(IHostEnvironment environment, Func<IHostEnvironment, bool> predicate) : ICondition
{
    /// <summary>
    /// Human-readable name of this condition for logging and debugging purposes
    /// </summary>
    public string Name => "Environment Condition";

    /// <summary>
    /// Evaluates whether seeding should execute based on the environment predicate.
    /// The predicate function is called with the current hosting environment to determine
    /// if the condition is met.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation if needed</param>
    /// <returns>
    /// True if the environment predicate returns true, indicating seeding should proceed.
    /// False if the predicate returns false or evaluation fails.
    /// </returns>
    public Task<bool> ShouldExecute(CancellationToken cancellationToken = default)
    {
        try
        {
            // Execute the predicate function with the current environment
            // This allows for flexible environment checking logic
            return Task.FromResult(predicate(environment));
        }
        catch (ArgumentException ex)
        {
            // Handle argument-related errors in predicate evaluation
            throw new InvalidOperationException($"Environment condition predicate failed: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log unexpected errors but allow seeding to proceed safely
            // Only catch non-cancellation exceptions to preserve cancellation semantics
            throw new InvalidOperationException($"Environment condition evaluation failed for condition '{Name}': {ex.Message}", ex);
        }
    }
}
