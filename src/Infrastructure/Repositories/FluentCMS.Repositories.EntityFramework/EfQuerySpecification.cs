using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FluentCMS.Repositories.EntityFramework;

/// <summary>
/// EF Core implementation of IQuerySpecification that wraps IQueryable
/// </summary>
public class EfQuerySpecification<T>(IQueryable<T> _queryable) : IQuerySpecification<T> where T : class
{
    protected IQueryable<T> Queryable => _queryable;

    // Apply a where predicate to filter results
    public IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate)
    {
        return new EfQuerySpecification<T>(_queryable.Where(predicate));
    }

    // Project results to a different type
    public IQuerySpecification<TResult> Select<TResult>(Expression<Func<T, TResult>> selector) where TResult : class
    {
        return new EfQuerySpecification<TResult>(_queryable.Select(selector));
    }

    // Remove duplicate results
    public IQuerySpecification<T> Distinct()
    {
        return new EfQuerySpecification<T>(_queryable.Distinct());
    }

    // Order by a key selector in ascending order
    public IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new EfQuerySpecification<T>(_queryable.OrderBy(keySelector));
    }

    // Order by a key selector in descending order
    public IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new EfQuerySpecification<T>(_queryable.OrderByDescending(keySelector));
    }

    // Apply a subsequent ordering in ascending order
    public IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        // Cast to IOrderedQueryable to use ThenBy
        var orderedQueryable = _queryable as IOrderedQueryable<T>;
        if (orderedQueryable == null)
        {
            throw new InvalidOperationException("ThenBy can only be called after OrderBy or OrderByDescending");
        }
        return new EfQuerySpecification<T>(orderedQueryable.ThenBy(keySelector));
    }

    // Apply a subsequent ordering in descending order
    public IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        // Cast to IOrderedQueryable to use ThenByDescending
        var orderedQueryable = _queryable as IOrderedQueryable<T>;
        if (orderedQueryable == null)
        {
            throw new InvalidOperationException("ThenByDescending can only be called after OrderBy or OrderByDescending");
        }
        return new EfQuerySpecification<T>(orderedQueryable.ThenByDescending(keySelector));
    }

    // Skip a specified number of elements
    public IQuerySpecification<T> Skip(int count)
    {
        return new EfQuerySpecification<T>(_queryable.Skip(count));
    }

    // Take a specified number of elements
    public IQuerySpecification<T> Take(int count)
    {
        return new EfQuerySpecification<T>(_queryable.Take(count));
    }

    // Group by a key selector
    public IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        // Note: GroupBy returns IQueryable<IGrouping<TKey, T>>, but we need IQueryable<T>
        // This is a simplified implementation that may need adjustment based on actual use case
        return new EfQuerySpecification<T>(_queryable);
    }

    // Get a single result or null with a predicate
    public async Task<T?> SingleOrDefault(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryable.SingleOrDefaultAsync(predicate, cancellationToken);
    }

    // Get a single result or null
    public async Task<T?> SingleOrDefault(CancellationToken cancellationToken = default)
    {
        return await _queryable.SingleOrDefaultAsync(cancellationToken);
    }

    // Get a single result (throws if not found or multiple)
    public async Task<T> Single(CancellationToken cancellationToken = default)
    {
        return await _queryable.SingleAsync(cancellationToken);
    }

    // Get a single result with a predicate (throws if not found or multiple)
    public async Task<T> Single(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryable.SingleAsync(predicate, cancellationToken);
    }

    // Get the first result or null with a predicate
    public async Task<T?> FirstOrDefault(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryable.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    // Get the first result or null
    public async Task<T?> FirstOrDefault(CancellationToken cancellationToken = default)
    {
        return await _queryable.FirstOrDefaultAsync(cancellationToken);
    }

    // Get the first result (throws if not found)
    public async Task<T> First(CancellationToken cancellationToken = default)
    {
        return await _queryable.FirstAsync(cancellationToken);
    }

    // Get the first result with a predicate (throws if not found)
    public async Task<T> First(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryable.FirstAsync(predicate, cancellationToken);
    }

    // Execute query and return results as a list
    public async Task<List<T>> ToList(CancellationToken cancellationToken = default)
    {
        return await _queryable.ToListAsync(cancellationToken);
    }

    // Execute query and return results as an array
    public async Task<T[]> ToArray(CancellationToken cancellationToken = default)
    {
        return await _queryable.ToArrayAsync(cancellationToken);
    }

    // Execute query and return results as an enumerable
    public async Task<IEnumerable<T>> ToEnumable(CancellationToken cancellationToken = default)
    {
        return await _queryable.ToListAsync(cancellationToken);
    }

    // Count the number of results with a predicate
    public async Task<int> Count(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryable.CountAsync(predicate, cancellationToken);
    }

    // Count the total number of results
    public async Task<int> Count(CancellationToken cancellationToken = default)
    {
        return await _queryable.CountAsync(cancellationToken);
    }

    // Check if any results exist
    public async Task<bool> Any(CancellationToken cancellationToken = default)
    {
        return await _queryable.AnyAsync(cancellationToken);
    }

    // Check if any results exist with a predicate
    public async Task<bool> Any(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await _queryable.AnyAsync(predicate, cancellationToken);
    }

    // Execute query with pagination and return paged result with metadata
    public async Task<IPagedResult<T>> ToPagedResult(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Ensure page is at least 1
        if (page < 1) page = 1;

        // Ensure page size is positive
        if (pageSize < 1) pageSize = 10;

        // Get total count before pagination
        var totalCount = await _queryable.CountAsync(cancellationToken);

        // Calculate skip amount
        var skip = (page - 1) * pageSize;

        // Get the page of results
        var items = await _queryable.Skip(skip).Take(pageSize).ToListAsync(cancellationToken);

        // Return paged result with metadata
        return new PagedResult<T>(items, totalCount, page, pageSize);
    }
}
