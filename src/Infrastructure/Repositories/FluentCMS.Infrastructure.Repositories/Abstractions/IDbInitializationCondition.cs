namespace FluentCMS.Infrastructure.Repositories.Abstractions;

/// <summary>
/// Defines a contract for conditional execution of seeding operations.
/// Conditions determine whether seeding/schema validation should proceed
/// based on environment, configuration, or other runtime factors.
/// </summary>
public interface IDbInitializationCondition
{
    /// <summary>
    /// Name of the condition for logging purposes
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Evaluates whether seeding operations should execute based on this condition.
    /// Multiple conditions are evaluated with AND logic by default.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if seeding should proceed, false to skip seeding</returns>
    Task<bool> ShouldExecute(CancellationToken cancellationToken = default);
}
