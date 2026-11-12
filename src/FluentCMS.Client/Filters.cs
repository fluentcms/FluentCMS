using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Client;

// The operators of the bracket grammar (spec 4.4), as the SDK names them.
public enum FilterOperator
{
    Equal,
    NotEqual,
    In,
    NotIn,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Between,
    Contains,
    StartsWith,
    IsNull,
    IsNotNull,
    HasBlock,
}

public abstract record ContentFilter
{
    private protected ContentFilter()
    {
    }

    private protected static int HashOf(IReadOnlyList<ContentFilter> filters)
    {
        var hash = new HashCode();
        foreach (var filter in filters)
        {
            hash.Add(filter);
        }

        return hash.ToHashCode();
    }
}

public sealed record FieldFilter(string Path, FilterOperator Operator, object? Value = null) : ContentFilter
{
    public bool Equals(FieldFilter? other) =>
        other is not null && Path == other.Path && Operator == other.Operator && SameOperand(Value, other.Value);

    public override int GetHashCode() => HashCode.Combine(Path, Operator);

    private static bool SameOperand(object? left, object? right) =>
        left is IEnumerable leftItems and not string && right is IEnumerable rightItems and not string
            ? leftItems.Cast<object?>().SequenceEqual(rightItems.Cast<object?>())
            : Equals(left, right);
}

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "And is the name the SDK gives the node.")]
public sealed record And(params IReadOnlyList<ContentFilter> Filters) : ContentFilter
{
    public bool Equals(And? other) => other is not null && Filters.SequenceEqual(other.Filters);

    public override int GetHashCode() => HashOf(Filters);
}

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Or is the name the SDK gives the node.")]
public sealed record Or(params IReadOnlyList<ContentFilter> Filters) : ContentFilter
{
    public bool Equals(Or? other) => other is not null && Filters.SequenceEqual(other.Filters);

    public override int GetHashCode() => HashOf(Filters);
}

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Not is the name the SDK gives the node.")]
public sealed record Not(ContentFilter Filter) : ContentFilter;

public sealed record SortField(string Path, bool Descending = false);
