namespace FluentCMS.Repositories.Abstractions;

public interface IDatabaseManager
{
    /// <summary>
    /// Gets the connection string used to connect to the database.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Checks if the specified entity sets exist in the database.
    /// This includes tables in RDBMS, collections in document databases, or other storage containers.
    /// </summary>
    Task<bool> EntitySetsExist(IEnumerable<string> entitySetNames, CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed database manager interface that provides compile-time database resolution with library-based markers.
/// The type parameter T must be a library marker interface that inherits from IDatabaseManagerMarker.
/// This constraint ensures type safety and prevents incorrect usage.
/// </summary>
/// <typeparam name="T">The library marker interface used to identify which database configuration to use.</typeparam>
public interface IDatabaseManager<T> : IDatabaseManager where T : IDatabaseScopeMarker
{
    // This interface extends IDatabaseManager but adds library-based type resolution
    // The generic constraint ensures only valid marker interfaces can be used
}
