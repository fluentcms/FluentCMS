namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base abstract class for implementing specifications
/// </summary>
public abstract class Specification<T> : ICompositeSpecification<T> where T : class, IEntity
{
    // Abstract method that derived classes must implement
    public abstract IQueryable<T> Apply(IQueryable<T> query);

    // Combines this specification with another using AND logic
    public ICompositeSpecification<T> And(ISpecification<T> other)
    {
        return new AndSpecification<T>(this, other);
    }

    // Combines this specification with another using OR logic
    public ICompositeSpecification<T> Or(ISpecification<T> other)
    {
        return new OrSpecification<T>(this, other);
    }

    // Negates this specification
    public ICompositeSpecification<T> Not()
    {
        return new NotSpecification<T>(this);
    }
}

/// <summary>
/// Specification that combines two specifications with AND logic
/// </summary>
internal class AndSpecification<T> : Specification<T> where T : class, IEntity
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public AndSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left;
        _right = right;
    }

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        // Apply both specifications sequentially (AND logic)
        return _right.Apply(_left.Apply(query));
    }
}

/// <summary>
/// Specification that combines two specifications with OR logic
/// </summary>
internal class OrSpecification<T> : Specification<T> where T : class, IEntity
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left;
        _right = right;
    }

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        // For OR logic, we need to build expressions and combine them
        // This is more complex as we need to work with expression trees
        var leftQuery = _left.Apply(query);
        var rightQuery = _right.Apply(query);

        // Note: This is a simplified implementation
        // A more sophisticated implementation would need to combine the actual expressions
        return leftQuery.Union(rightQuery);
    }
}

/// <summary>
/// Specification that negates another specification
/// </summary>
internal class NotSpecification<T> : Specification<T> where T : class, IEntity
{
    private readonly ISpecification<T> _specification;

    public NotSpecification(ISpecification<T> specification)
    {
        _specification = specification;
    }

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        // For NOT logic, we need to negate the expression
        // This is a simplified implementation
        // A more sophisticated implementation would extract and negate the actual expressions
        var allIds = query.Select(x => x.Id);
        var specificationIds = _specification.Apply(query).Select(x => x.Id);
        return query.Where(x => !specificationIds.Contains(x.Id));
    }
}

/// <summary>
/// Simple expression-based specification
/// </summary>
public class ExpressionSpecification<T> : Specification<T> where T : class, IEntity
{
    private readonly Expression<Func<T, bool>> _expression;

    public ExpressionSpecification(Expression<Func<T, bool>> expression)
    {
        _expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        return query.Where(_expression);
    }
}
