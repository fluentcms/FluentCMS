namespace FluentCMS.Repositories.Abstractions;

/// <summary>
/// Base abstract class for implementing specifications
/// </summary>
public abstract class Specification<T> : ISpecification<T> where T : class
{
    // Abstract method that derived classes must implement
    public abstract IQueryable<T> Apply(IQueryable<T> query);
}

/// <summary>
/// Simple expression-based specification
/// </summary>
public class ExpressionSpecification<T>(Expression<Func<T, bool>> expression) : Specification<T> where T : class
{
    private readonly Expression<Func<T, bool>> _expression = expression ?? throw new ArgumentNullException(nameof(expression));

    public override IQueryable<T> Apply(IQueryable<T> query)
    {
        return query.Where(_expression);
    }
}
