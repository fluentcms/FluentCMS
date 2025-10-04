namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Defines a contract for initializing database with seed data.
/// Implementations should execute all registered data seeders across all areas.
/// </summary>
public interface IDataInitializer
{
    /// <summary>
    /// Seeds the database with initial data across all registered seeders.
    /// Seeders are executed in order of priority without considering the TArea.
    /// </summary>
    Task InitializeAll(CancellationToken cancellationToken = default);
}
