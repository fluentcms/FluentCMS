namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base interface for all specifications
/// </summary>
public interface ISpecification<T> where T : class
{
    // Applies the specification to a queryable source
    IQueryable<T> Apply(IQueryable<T> query);
}

/// <summary>
/// Interface for fluent query building similar to LINQ
/// </summary>
public interface IQuerySpecification<T> where T : class
{
    // Filtering
    IQuerySpecification<T> Where(Expression<Func<T, bool>> predicate);
    
    // Ordering
    IQuerySpecification<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IQuerySpecification<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);
    IQuerySpecification<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector);
    IQuerySpecification<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector);
    
    // Pagination
    IQuerySpecification<T> Skip(int count);
    IQuerySpecification<T> Take(int count);
    
    // Grouping
    IQuerySpecification<T> GroupBy<TKey>(Expression<Func<T, TKey>> keySelector);
    
    // Distinct
    IQuerySpecification<T> Distinct();
    
    // Build the final queryable
    IQueryable<T> Build(IQueryable<T> source);
    
    // Convert to specification for repository usage
    ISpecification<T> ToSpecification();
}
