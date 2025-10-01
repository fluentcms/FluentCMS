namespace FluentCMS.Repositories.EntityFramework.Services;

/// <summary>
/// Orchestrates database initialization including schema validation and data seeding
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Initializes all configured databases
    /// Runs schema validators first, then data seeders based on their configurations
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task InitializeAll(CancellationToken cancellationToken = default);
}
