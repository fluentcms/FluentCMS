using FluentCMS.Database.Abstractions;

namespace FluentCMS.Database.Extensions;

/// <summary>
/// Typed wrapper for IDatabaseManager that implements IDatabaseManager&lt;T&gt; with library marker constraint.
/// This provides compile-time type safety while delegating to the actual database manager implementation.
/// </summary>
/// <typeparam name="T">The library marker interface used to identify the database configuration.</typeparam>
internal sealed class TypedDatabaseManager<T>(IDatabaseManager inner) : IDatabaseManager<T> where T : IDatabaseManagerMarker
{
    private readonly IDatabaseManager _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<bool> DatabaseExists(CancellationToken cancellationToken = default)
        => _inner.DatabaseExists(cancellationToken);

    public Task CreateDatabase(CancellationToken cancellationToken = default)
        => _inner.CreateDatabase(cancellationToken);

    public Task<bool> TablesExist(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
        => _inner.TablesExist(tableNames, cancellationToken);

    public Task<bool> TablesEmpty(IEnumerable<string> tableNames, CancellationToken cancellationToken = default)
        => _inner.TablesEmpty(tableNames, cancellationToken);

    public Task ExecuteRaw(string sql, CancellationToken cancellationToken = default)
        => _inner.ExecuteRaw(sql, cancellationToken);
}
