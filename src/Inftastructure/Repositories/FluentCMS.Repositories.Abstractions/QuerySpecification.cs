namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Fluent query specification builder that provides LINQ-like syntax
/// </summary>
public class QuerySpecification<T> : IQuerySpecification<T> where T : class
{
    private readonly List<Func<IQueryable<T>, IQueryable<T>>> _operations;

    public QuerySpecification()
    {
        _operations = [];
    }

    private QuerySpecification(List<Func<IQueryable<T>, IQueryable<T>>> operations)
    {
        _operations = [.. operations];
    }

    // Adds a where clause to the query
    public IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.Where(predicate)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds an order by clause to the query
    public IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.OrderBy(keySelector)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds an order by descending clause to the query
    public IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.OrderByDescending(keySelector)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds a then by clause to the query (requires previous ordering)
    public IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => ((IOrderedQueryable<T>)query).ThenBy(keySelector)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds a then by descending clause to the query (requires previous ordering)
    public IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => ((IOrderedQueryable<T>)query).ThenByDescending(keySelector)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds a skip operation for pagination
    public IQuerySpecification<T> Skip(int count)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.Skip(count)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds a take operation for pagination
    public IQuerySpecification<T> Take(int count)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.Take(count)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds a group by operation
    public IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.GroupBy(keySelector).SelectMany(g => g)
        };
        return new QuerySpecification<T>(newOperations);
    }

    // Adds a distinct operation to remove duplicates
    public IQuerySpecification<T> Distinct()
    {
        var newOperations = new List<Func<IQueryable<T>, IQueryable<T>>>(_operations)
        {
            query => query.Distinct()
        };
        return new QuerySpecification<T>(newOperations);
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
internal class QueryBasedSpecification<T> : Specification<T> where T : class
{
    private readonly IQuerySpecification<T> _querySpecification;

    public QueryBasedSpecification(IQuerySpecification<T> querySpecification)
    {
        _querySpecification = querySpecification;
    }

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        return _querySpecification.Build(query);
    }
}
