namespace FluentCMS.DataSeeding.Abstractions;

/// <summary>
/// Defines a contract for seeding data into a database.
/// Implementations should provide priority-based execution and existence checking.
/// </summary>
public interface IDataSeeder
{
    /// <summary>
    /// Execution priority for this seeder. Lower numbers execute first.
    /// Use gaps (10, 20, 30) to allow future insertion without reordering.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Checks if the data this seeder is responsible for already exists.
    /// This enables idempotent seeding operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>True if data exists and seeding should be skipped, false otherwise</returns>
    Task<bool> HasData(CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeds the data into the database. This method should only be called
    /// if HasData returns false, ensuring idempotent operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    Task SeedData(CancellationToken cancellationToken = default);
}
