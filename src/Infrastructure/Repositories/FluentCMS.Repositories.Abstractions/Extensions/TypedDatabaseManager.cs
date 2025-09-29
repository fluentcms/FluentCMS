using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Database.Extensions;

/// <summary>
/// Typed wrapper for IDatabaseManager that implements IDatabaseManager&lt;T&gt; with library marker constraint.
/// This provides compile-time type safety while delegating to the actual database manager implementation.
/// </summary>
/// <typeparam name="T">The library marker interface used to identify the database configuration.</typeparam>
internal sealed class TypedDatabaseManager<T>(IDatabaseManager inner) : IDatabaseManager<T> where T : IDatabaseScopeMarker
{
    private readonly IDatabaseManager _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    public string ConnectionString => inner?.ConnectionString ?? throw new InvalidOperationException("Inner database manager is not initialized.");

    public Task<bool> EntitySetsExist(IEnumerable<string> entitySetNames, CancellationToken cancellationToken = default)
        => _inner.EntitySetsExist(entitySetNames, cancellationToken);
}
