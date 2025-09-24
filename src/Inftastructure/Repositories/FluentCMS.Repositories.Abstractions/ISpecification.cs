using System.Linq.Expressions;

namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base interface for all specifications
/// </summary>
public interface ISpecification<T> where T : class, IEntity
{
    // Applies the specification to a queryable source
    IQueryable<T> Apply(IQueryable<T> query);
}

/// <summary>
/// Interface for composable specifications that can be combined with logical operators
/// </summary>
public interface ICompositeSpecification<T> : ISpecification<T> where T : class, IEntity
{
    // Combines this specification with another using AND logic
    ICompositeSpecification<T> And(ISpecification<T> other);
    
    // Combines this specification with another using OR logic
    ICompositeSpecification<T> Or(ISpecification<T> other);
    
    // Negates this specification
    ICompositeSpecification<T> Not();
}

/// <summary>
/// Interface for fluent query building similar to LINQ
/// </summary>
public interface IQuerySpecification<T> where T : class, IEntity
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
    
    // Projection - returns a projection specification for any result type
    IProjectionSpecification<TResult> Select<TResult>(Expression<Func<T, TResult>> selector);
    
    // Distinct
    IQuerySpecification<T> Distinct();
    
    // Build the final queryable
    IQueryable<T> Build(IQueryable<T> source);
    
    // Convert to specification for repository usage
    ISpecification<T> ToSpecification();
}

/// <summary>
/// Interface for query projections that can return any type (not just entities)
/// </summary>
public interface IProjectionSpecification<TResult>
{
    // Continue chaining operations on projected results
    IProjectionSpecification<TResult> Where(Expression<Func<TResult, bool>> predicate);
    IProjectionSpecification<TResult> OrderBy<TKey>(Expression<Func<TResult, TKey>> keySelector);
    IProjectionSpecification<TResult> OrderByDescending<TKey>(Expression<Func<TResult, TKey>> keySelector);
    IProjectionSpecification<TResult> ThenBy<TKey>(Expression<Func<TResult, TKey>> keySelector);
    IProjectionSpecification<TResult> ThenByDescending<TKey>(Expression<Func<TResult, TKey>> keySelector);
    IProjectionSpecification<TResult> Skip(int count);
    IProjectionSpecification<TResult> Take(int count);
    IProjectionSpecification<TResult> Distinct();
    
    // Further projections
    IProjectionSpecification<TNewResult> Select<TNewResult>(Expression<Func<TResult, TNewResult>> selector);
    
    // Build the final queryable
    IQueryable<TResult> Build<T>(IQueryable<T> source) where T : class, IEntity;
}
