using System.Linq.Expressions;

namespace FluentCMS.Repositories.Abstractions;

// Comprehensive query interface for repository operations
// Provides LINQ-style querying capabilities with MongoDB and EF Core compatibility
public interface IQuerySpecification<T> where T : class
{
    // LINQ-style filtering and projection
    IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate);
    IQuerySpecification<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class;
    IQuerySpecification<T> Distinct();

    // Ordering operations (returns IRepositoryOrderedQuery<T> for proper ordering constraints)
    IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

    // Terminal operations that don't require ordering
    Task<T?> SingleOrDefault(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<T?> SingleOrDefault(CancellationToken cancellationToken = default);
    Task<T> Single(CancellationToken cancellationToken = default);
    Task<T> Single(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<List<T>> ToList(CancellationToken cancellationToken = default);
    Task<T[]> ToArray(CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> ToEnumable(CancellationToken cancellationToken = default);

    // Aggregate operations
    Task<int> Count(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> Count(CancellationToken cancellationToken = default);
    Task<bool> Any(CancellationToken cancellationToken = default);
    Task<bool> Any(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector);

    // Additional ordering operations
    IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector);
    
    // Operations that REQUIRE ordering for deterministic results
    IQuerySpecification<T> Skip(int count);
    IQuerySpecification<T> Take(int count);

    // Pagination method for ordered queries
    Task<IPagedResult<T>> ToPagedResult(int page, int pageSize, CancellationToken cancellationToken = default);

    // First/Last operations (predictable with ordering)
    Task<T?> FirstOrDefault(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<T?> FirstOrDefault(CancellationToken cancellationToken = default);
    Task<T> First(CancellationToken cancellationToken = default);
    Task<T> First(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
}
