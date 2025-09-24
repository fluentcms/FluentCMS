namespace FluentCMS.Repositories.Abstractions;

public interface ITransactionalRepository : IRepository
{
    /// <summary>
    /// Begins a new transaction scope
    /// </summary>
    Task BeginTransaction(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current transaction
    /// </summary>
    Task Commit(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction
    /// </summary>
    Task Rollback(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether a transaction is currently active
    /// </summary>
    bool IsTransactionActive { get; }
}

public interface ITransactionalRepository<TEntity> : ITransactionalRepository, IRepository<TEntity> where TEntity : class, IEntity
{
}
