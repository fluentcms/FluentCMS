namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Extension methods to make working with specifications easier
/// </summary>
public static class SpecificationExtensions
{
    // Creates a specification from a simple expression
    public static ISpecification<T> Where<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
    {
        return new ExpressionSpecification<T>(predicate);
    }

    // Extension method to create query specification from repository
    public static IQuerySpecification<T> Query<T>(this IRepository<T> repository) where T : class, IEntity
    {
        return new QuerySpecification<T>();
    }

    // Helper to convert expression to specification
    public static ISpecification<T> ToSpecification<T>(this Expression<Func<T, bool>> predicate) where T : class, IEntity
    {
        return new ExpressionSpecification<T>(predicate);
    }

    // Pagination helper for query specifications
    public static IQuerySpecification<T> Paginate<T>(this IQuerySpecification<T> specification, int page, int pageSize) where T : class, IEntity
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        
        return specification.Skip((page - 1) * pageSize).Take(pageSize);
    }

    // Ordering helpers
    public static IQuerySpecification<T> OrderByProperty<T>(this IQuerySpecification<T> specification, string propertyName, bool descending = false) where T : class, IEntity
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, propertyName);
        var lambda = Expression.Lambda(property, parameter);

        if (descending)
        {
            var method = typeof(IQuerySpecification<T>).GetMethod(nameof(IQuerySpecification<T>.OrderByDescending));
            var genericMethod = method!.MakeGenericMethod(property.Type);
            return (IQuerySpecification<T>)genericMethod.Invoke(specification, new object[] { lambda })!;
        }
        else
        {
            var method = typeof(IQuerySpecification<T>).GetMethod(nameof(IQuerySpecification<T>.OrderBy));
            var genericMethod = method!.MakeGenericMethod(property.Type);
            return (IQuerySpecification<T>)genericMethod.Invoke(specification, new object[] { lambda })!;
        }
    }
}

/// <summary>
/// Pagination result wrapper
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
