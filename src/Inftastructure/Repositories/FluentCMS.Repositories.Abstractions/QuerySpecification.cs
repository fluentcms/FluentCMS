namespace FluentCMS.Repositories;

/// <summary>
/// Fluent query specification builder that provides LINQ-like syntax
/// </summary>
public class QuerySpecification<T> : IQuerySpecification<T> where T : class
{
    private readonly ImmutableList<Func<IQueryable<T>, IQueryable<T>>> _operations;
    private readonly bool _hasOrdering;

    public QuerySpecification()
    {
        _operations = ImmutableList<Func<IQueryable<T>, IQueryable<T>>>.Empty;
        _hasOrdering = false;
    }

    private QuerySpecification(ImmutableList<Func<IQueryable<T>, IQueryable<T>>> operations, bool hasOrdering = false)
    {
        _operations = operations;
        _hasOrdering = hasOrdering;
    }

    // Adds a where clause to the query
    public IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var newOperations = _operations.Add(query => query.Where(predicate));
        return new QuerySpecification<T>(newOperations, _hasOrdering);
    }

    // Adds an order by clause to the query
    public IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        var newOperations = _operations.Add(query => query.OrderBy(keySelector));
        return new QuerySpecification<T>(newOperations, hasOrdering: true);
    }

    // Adds an order by descending clause to the query
    public IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        var newOperations = _operations.Add(query => query.OrderByDescending(keySelector));
        return new QuerySpecification<T>(newOperations, hasOrdering: true);
    }

    // Adds a then by clause to the query (requires previous ordering)
    public IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        if (!_hasOrdering)
        {
            throw new InvalidOperationException("ThenBy can only be used after OrderBy or OrderByDescending has been called.");
        }
        
        var newOperations = _operations.Add(query => ((IOrderedQueryable<T>)query).ThenBy(keySelector));
        return new QuerySpecification<T>(newOperations, hasOrdering: true);
    }

    // Adds a then by descending clause to the query (requires previous ordering)
    public IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        if (!_hasOrdering)
        {
            throw new InvalidOperationException("ThenByDescending can only be used after OrderBy or OrderByDescending has been called.");
        }
        
        var newOperations = _operations.Add(query => ((IOrderedQueryable<T>)query).ThenByDescending(keySelector));
        return new QuerySpecification<T>(newOperations, hasOrdering: true);
    }

    // Adds a skip operation for pagination
    public IQuerySpecification<T> Skip(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        }
        
        var newOperations = _operations.Add(query => query.Skip(count));
        return new QuerySpecification<T>(newOperations, _hasOrdering);
    }

    // Adds a take operation for pagination
    public IQuerySpecification<T> Take(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        }
        
        var newOperations = _operations.Add(query => query.Take(count));
        return new QuerySpecification<T>(newOperations, _hasOrdering);
    }

    // Adds a group by operation
    public IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        var newOperations = _operations.Add(query => query.GroupBy(keySelector).SelectMany(g => g));
        return new QuerySpecification<T>(newOperations, _hasOrdering);
    }

    // Adds a distinct operation to remove duplicates
    public IQuerySpecification<T> Distinct()
    {
        var newOperations = _operations.Add(query => query.Distinct());
        return new QuerySpecification<T>(newOperations, _hasOrdering);
    }

    // Builds the final queryable by applying all operations
    public IQueryable<T> Build(IQueryable<T> source)
    {
        return _operations.Aggregate(source, (current, operation) => operation(current));
    }

    // Converts this query specification to a reusable specification
    public ISpecification<T> ToSpecification()
    {
        return new QueryBasedSpecification<T>(this);
    }
}

/// <summary>
/// Specification wrapper for query specifications
/// </summary>
internal class QueryBasedSpecification<T>(IQuerySpecification<T> querySpecification) : Specification<T> where T : class
{
    private readonly IQuerySpecification<T> _querySpecification = querySpecification;

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        return _querySpecification.Build(query);
    }
}

/// <summary>
/// Fluent repository query wrapper that provides direct execution capabilities
/// </summary>
public class RepositoryQuery<T> : IQuerySpecification<T> where T : class, IEntity
{
    private readonly IRepository<T> _repository;
    private readonly IQuerySpecification<T> _specification;

    public RepositoryQuery(IRepository<T> repository) : this(repository, new QuerySpecification<T>())
    {
    }

    private RepositoryQuery(IRepository<T> repository, IQuerySpecification<T> specification)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _specification = specification ?? throw new ArgumentNullException(nameof(specification));
    }

    // IQuerySpecification<T> implementation - delegate to internal specification
    public IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate)
    {
        return new RepositoryQuery<T>(_repository, _specification.Where(predicate));
    }

    public IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new RepositoryQuery<T>(_repository, _specification.OrderBy(keySelector));
    }

    public IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new RepositoryQuery<T>(_repository, _specification.OrderByDescending(keySelector));
    }

    public IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new RepositoryQuery<T>(_repository, _specification.ThenBy(keySelector));
    }

    public IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new RepositoryQuery<T>(_repository, _specification.ThenByDescending(keySelector));
    }

    public IQuerySpecification<T> Skip(int count)
    {
        return new RepositoryQuery<T>(_repository, _specification.Skip(count));
    }

    public IQuerySpecification<T> Take(int count)
    {
        return new RepositoryQuery<T>(_repository, _specification.Take(count));
    }

    public IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        return new RepositoryQuery<T>(_repository, _specification.GroupBy(keySelector));
    }

    public IQuerySpecification<T> Distinct()
    {
        return new RepositoryQuery<T>(_repository, _specification.Distinct());
    }

    public IQueryable<T> Build(IQueryable<T> source)
    {
        return _specification.Build(source);
    }

    public ISpecification<T> ToSpecification()
    {
        return _specification.ToSpecification();
    }

    // Direct execution methods that use the repository
    public async Task<IEnumerable<T>> Query(CancellationToken cancellationToken = default)
    {
        return await _repository.Query(ToSpecification(), cancellationToken);
    }

    public async Task<T?> FirstOrDefault(CancellationToken cancellationToken = default)
    {
        return await _repository.FirstOrDefault(ToSpecification(), cancellationToken);
    }

    public async Task<T?> SingleOrDefault(CancellationToken cancellationToken = default)
    {
        return await _repository.SingleOrDefault(ToSpecification(), cancellationToken);
    }

    public async Task<long> Count(CancellationToken cancellationToken = default)
    {
        return await _repository.Count(ToSpecification(), cancellationToken);
    }

    public async Task<bool> Any(CancellationToken cancellationToken = default)
    {
        return await _repository.Any(ToSpecification(), cancellationToken);
    }

    public async Task<PagedResult<T>> FindPaged(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return await _repository.FindPaged(ToSpecification(), page, pageSize, cancellationToken);
    }

    public async Task<decimal> Sum(Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default)
    {
        return await _repository.Sum(ToSpecification(), selector, cancellationToken);
    }
}
