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

    // Combines multiple specifications with AND logic
    public static ICompositeSpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right) where T : class, IEntity
    {
        if (left is ICompositeSpecification<T> composite)
            return composite.And(right);

        return new ExpressionSpecification<T>(x => true).And(left).And(right);
    }

    // Combines multiple specifications with OR logic
    public static ICompositeSpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right) where T : class, IEntity
    {
        if (left is ICompositeSpecification<T> composite)
            return composite.Or(right);

        return new ExpressionSpecification<T>(x => true).And(left).Or(right);
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
/// Common specifications that can be reused across different entities
/// </summary>
public static class CommonSpecifications
{
    // Specification for entities with a specific ID
    public static ISpecification<T> ById<T>(Guid id) where T : class, IEntity
    {
        return new ExpressionSpecification<T>(x => x.Id == id);
    }

    // Specification for entities with IDs in a list
    public static ISpecification<T> ByIds<T>(IEnumerable<Guid> ids) where T : class, IEntity
    {
        var idList = ids.ToList();
        return new ExpressionSpecification<T>(x => idList.Contains(x.Id));
    }

    // Specification that matches all entities (useful as a starting point)
    public static ISpecification<T> All<T>() where T : class, IEntity
    {
        return new ExpressionSpecification<T>(x => true);
    }

    // Specification that matches no entities
    public static ISpecification<T> None<T>() where T : class, IEntity
    {
        return new ExpressionSpecification<T>(x => false);
    }
}

/// <summary>
/// Pagination result wrapper
/// </summary>
public class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// Repository extensions for common operations
/// </summary>
public static class RepositorySpecificationExtensions
{
    // Find entities with pagination
    public static async Task<PagedResult<T>> FindPaged<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        // Get total count first
        var totalCount = await repository.Count(specification, cancellationToken);

        // Create a combined specification that includes the original specification and pagination
        // This is a simplified approach since we removed AsQueryable()
        var allItems = await repository.Find(specification, cancellationToken);
        var pagedItems = allItems.Skip((page - 1) * pageSize).Take(pageSize);

        return new PagedResult<T>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    // Check if any entity exists matching the specification
    public static async Task<bool> Exists<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        return await repository.Any(specification, cancellationToken);
    }

    // Get first entity or throw if not found
    public static async Task<T> GetFirst<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        var result = await repository.FindFirst(specification, cancellationToken);
        return result ?? throw new InvalidOperationException("No entity found matching the specification");
    }

    // Get single entity or throw if not found or multiple found
    public static async Task<T> GetSingle<T>(
        this IRepository<T> repository,
        ISpecification<T> specification,
        CancellationToken cancellationToken = default) where T : class, IEntity
    {
        var result = await repository.FindSingle(specification, cancellationToken);
        return result ?? throw new InvalidOperationException("No entity found matching the specification");
    }
}
