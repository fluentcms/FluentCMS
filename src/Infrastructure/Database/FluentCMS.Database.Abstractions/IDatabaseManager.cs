namespace FluentCMS.Database.Abstractions;

public interface IDatabaseManager
{
    /// <summary>
    /// Checks if the database exists.
    /// </summary>
    Task<bool> DatabaseExists(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the database if it does not already exist.
    /// </summary>
    Task CreateDatabase(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the specified tables exist in the database.
    /// </summary>
    Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the specified tables are empty.
    /// </summary>
    Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a raw SQL command against the database.
    /// </summary>
    Task ExecuteRaw(string sql, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed database manager interface that provides compile-time database resolution with library-based markers.
/// The type parameter T must be a library marker interface that inherits from IDatabaseManagerMarker.
/// This constraint ensures type safety and prevents incorrect usage.
/// </summary>
/// <typeparam name="T">The library marker interface used to identify which database configuration to use.</typeparam>
public interface IDatabaseManager<T> : IDatabaseManager where T : IDatabaseManagerMarker
{
    // This interface extends IDatabaseManager but adds library-based type resolution
    // The generic constraint ensures only valid marker interfaces can be used
}
