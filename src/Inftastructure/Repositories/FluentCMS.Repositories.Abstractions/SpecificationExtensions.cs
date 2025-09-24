namespace FluentCMS.Repositories;

/// <summary>
/// Extension methods to make working with specifications easier
/// </summary>
public static class SpecificationExtensions
{
    // Cache for compiled property access expressions to improve performance
    private static readonly ConcurrentDictionary<string, LambdaExpression> _propertyExpressionCache = new();
    
    // Cache for property information to avoid repeated reflection calls
    private static readonly ConcurrentDictionary<string, PropertyInfo> _propertyInfoCache = new();
    // Creates a specification from a simple expression
    public static ISpecification<T> Where<T>(Expression<Func<T, bool>> predicate) where T : class, IEntity
    {
        return new ExpressionSpecification<T>(predicate);
    }

    // Extension method to create fluent repository query
    public static RepositoryQuery<T> Query<T>(this IRepository<T> repository) where T : class, IEntity
    {
        return new RepositoryQuery<T>(repository);
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

    // Ordering helpers with improved safety and performance
    public static IQuerySpecification<T> OrderByProperty<T>(this IQuerySpecification<T> specification, string propertyName, bool descending = false) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(specification);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name cannot be null or empty.", nameof(propertyName));
        }

        var cacheKey = $"{typeof(T).FullName}.{propertyName}";
        
        // Get or create cached property info
        var propertyInfo = _propertyInfoCache.GetOrAdd(cacheKey, _ => 
        {
            var prop = typeof(T).GetProperty(propertyName);
            if (prop == null)
            {
                throw new ArgumentException($"Property '{propertyName}' not found on type '{typeof(T).Name}'.", nameof(propertyName));
            }
            
            if (!prop.CanRead)
            {
                throw new ArgumentException($"Property '{propertyName}' on type '{typeof(T).Name}' is not readable.", nameof(propertyName));
            }
            
            return prop;
        });

        // Get or create cached lambda expression
        var lambda = _propertyExpressionCache.GetOrAdd(cacheKey, _ =>
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, propertyInfo);
            return Expression.Lambda(property, parameter);
        });

        try
        {
            if (descending)
            {
                var method = typeof(IQuerySpecification<T>).GetMethod(nameof(IQuerySpecification<T>.OrderByDescending));
                var genericMethod = method?.MakeGenericMethod(propertyInfo.PropertyType);
                return (IQuerySpecification<T>)(genericMethod?.Invoke(specification, new object[] { lambda }) 
                    ?? throw new InvalidOperationException($"Failed to invoke OrderByDescending for property '{propertyName}'."));
            }
            else
            {
                var method = typeof(IQuerySpecification<T>).GetMethod(nameof(IQuerySpecification<T>.OrderBy));
                var genericMethod = method?.MakeGenericMethod(propertyInfo.PropertyType);
                return (IQuerySpecification<T>)(genericMethod?.Invoke(specification, new object[] { lambda }) 
                    ?? throw new InvalidOperationException($"Failed to invoke OrderBy for property '{propertyName}'."));
            }
        }
        catch (Exception ex) when (ex is not ArgumentException and not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to create ordering expression for property '{propertyName}' on type '{typeof(T).Name}'.", ex);
        }
    }

    // Additional overload for better type safety when property type is known
    public static IQuerySpecification<T> OrderByProperty<T, TProperty>(this IQuerySpecification<T> specification, Expression<Func<T, TProperty>> propertyExpression, bool descending = false) where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(propertyExpression);

        return descending 
            ? specification.OrderByDescending(propertyExpression)
            : specification.OrderBy(propertyExpression);
    }
}
