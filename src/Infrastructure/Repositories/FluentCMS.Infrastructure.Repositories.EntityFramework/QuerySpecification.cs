namespace FluentCMS.Infrastructure.Repositories.EntityFramework;

public class QuerySpecification<TEntity> : IQuerySpecification<TEntity>
    where TEntity : class
{
    private readonly IQueryable<TEntity> _queryable;
    private readonly bool _isOrdered;

    public QuerySpecification(IQueryable<TEntity> queryable)
    {
        _queryable = queryable;
        _isOrdered = false;
    }

    private QuerySpecification(IQueryable<TEntity> queryable, bool isOrdered)
    {
        _queryable = queryable;
        _isOrdered = isOrdered;
    }

    // LINQ-style filtering
    public IQuerySpecification<TEntity> Where(Expression<Func<TEntity, bool>> predicate) =>
        new QuerySpecification<TEntity>(_queryable.Where(predicate), _isOrdered);

    public IQuerySpecification<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) where TResult : class =>
        new QuerySpecification<TResult>(_queryable.Select(selector), false);

    public IQuerySpecification<TEntity> Distinct() =>
        new QuerySpecification<TEntity>(_queryable.Distinct(), _isOrdered);

    // Ordering operations - these make the query ordered
    public IQuerySpecification<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        new QuerySpecification<TEntity>(_queryable.OrderBy(keySelector), true);

    public IQuerySpecification<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
        new QuerySpecification<TEntity>(_queryable.OrderByDescending(keySelector), true);

    public IQuerySpecification<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        var orderedQuery = _isOrdered ? ((IOrderedQueryable<TEntity>)_queryable).ThenBy(keySelector) : _queryable.OrderBy(keySelector);
        return new QuerySpecification<TEntity>(orderedQuery, true);
    }

    public IQuerySpecification<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
    {
        var orderedQuery = _isOrdered ? ((IOrderedQueryable<TEntity>)_queryable).ThenByDescending(keySelector) : _queryable.OrderByDescending(keySelector);
        return new QuerySpecification<TEntity>(orderedQuery, true);
    }

    // Operations that REQUIRE ordering
    public IQuerySpecification<TEntity> Skip(int count)
    {
        if (!_isOrdered)
            throw new InvalidOperationException("Skip requires ordering to be applied first. Use OrderBy/OrderByDescending.");
        return new QuerySpecification<TEntity>(_queryable.Skip(count), true);
    }

    public IQuerySpecification<TEntity> Take(int count)
    {
        if (!_isOrdered)
            throw new InvalidOperationException("Take requires ordering to be applied first. Use OrderBy/OrderByDescending.");
        return new QuerySpecification<TEntity>(_queryable.Take(count), true);
    }

    // Terminal operations
    public async Task<TEntity?> SingleOrDefault(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _queryable.SingleOrDefaultAsync(predicate, cancellationToken);

    public async Task<TEntity?> SingleOrDefault(CancellationToken cancellationToken = default) =>
        await _queryable.SingleOrDefaultAsync(cancellationToken);

    public async Task<TEntity> Single(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _queryable.SingleAsync(predicate, cancellationToken);

    public async Task<TEntity> Single(CancellationToken cancellationToken = default) =>
        await _queryable.SingleAsync(cancellationToken);

    public async Task<List<TEntity>> ToList(CancellationToken cancellationToken = default) =>
        await _queryable.ToListAsync(cancellationToken);

    public async Task<TEntity[]> ToArray(CancellationToken cancellationToken = default) =>
        await _queryable.ToArrayAsync(cancellationToken);

    // Aggregate operations
    public async Task<int> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _queryable.CountAsync(predicate, cancellationToken);

    public async Task<int> Count(CancellationToken cancellationToken = default) =>
        await _queryable.CountAsync(cancellationToken);

    public async Task<bool> Any(CancellationToken cancellationToken = default) =>
        await _queryable.AnyAsync(cancellationToken);

    public async Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
        await _queryable.AnyAsync(predicate, cancellationToken);

    // First/Last operations - require ordering for deterministic results
    private void ValidateOrderingForFirstLast(string operation)
    {
        if (!_isOrdered)
            throw new InvalidOperationException($"{operation} requires ordering for deterministic results. Use OrderBy/OrderByDescending first.");
    }

    public async Task<TEntity?> FirstOrDefault(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ValidateOrderingForFirstLast("FirstOrDefault with predicate");
        return await _queryable.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<TEntity?> FirstOrDefault(CancellationToken cancellationToken = default)
    {
        ValidateOrderingForFirstLast("FirstOrDefault");
        return await _queryable.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TEntity> First(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        ValidateOrderingForFirstLast("First with predicate");
        return await _queryable.FirstAsync(predicate, cancellationToken);
    }

    public async Task<TEntity> First(CancellationToken cancellationToken = default)
    {
        ValidateOrderingForFirstLast("First");
        return await _queryable.FirstAsync(cancellationToken);
    }

    // Pagination
    public async Task<IPagedResult<TEntity>> ToPagedResult(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_isOrdered)
            throw new InvalidOperationException("ToPagedResult requires ordering. Use OrderBy/OrderByDescending first.");

        var totalCount = await _queryable.LongCountAsync(cancellationToken);
        var items = await _queryable
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>(items, totalCount, page, pageSize);
    }

    public IAsyncEnumerable<TEntity> AsAsyncEnumerable(CancellationToken cancellationToken = default)
    {
        return _queryable.AsAsyncEnumerable();
    }
}
