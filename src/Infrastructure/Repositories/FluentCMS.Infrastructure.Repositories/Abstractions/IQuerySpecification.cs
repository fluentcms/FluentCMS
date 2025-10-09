namespace FluentCMS.Infrastructure.Repositories.Abstractions;

// Comprehensive query interface for repository operations
public interface IQuerySpecification<TEntity>
    where TEntity : class
{
    // LINQ-style filtering and projection
    IQuerySpecification<TEntity> Where(Expression<Func<TEntity, bool>> predicate);
    IQuerySpecification<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) where TResult : class;
    IQuerySpecification<TEntity> Distinct();

    // Ordering operations (returns IRepositoryOrderedQuery<T> for proper ordering constraints)
    IQuerySpecification<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);
    IQuerySpecification<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

    // Terminal operations that don't require ordering
    Task<TEntity?> SingleOrDefault(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<TEntity?> SingleOrDefault(CancellationToken cancellationToken = default);
    Task<TEntity> Single(CancellationToken cancellationToken = default);
    Task<TEntity> Single(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<List<TEntity>> ToList(CancellationToken cancellationToken = default);
    Task<TEntity[]> ToArray(CancellationToken cancellationToken = default);
    IAsyncEnumerable<TEntity> AsAsyncEnumerable(CancellationToken cancellationToken = default);

    // Aggregate operations
    Task<int> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> Count(CancellationToken cancellationToken = default);
    Task<bool> Any(CancellationToken cancellationToken = default);
    Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    // Additional ordering operations
    IQuerySpecification<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);
    IQuerySpecification<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

    // Operations that REQUIRE ordering for deterministic results
    IQuerySpecification<TEntity> Skip(int count);
    IQuerySpecification<TEntity> Take(int count);

    // Pagination method for ordered queries
    Task<IPagedResult<TEntity>> ToPagedResult(int page, int pageSize, CancellationToken cancellationToken = default);

    // First/Last operations (predictable with ordering)
    Task<TEntity?> FirstOrDefault(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<TEntity?> FirstOrDefault(CancellationToken cancellationToken = default);
    Task<TEntity> First(CancellationToken cancellationToken = default);
    Task<TEntity> First(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}
