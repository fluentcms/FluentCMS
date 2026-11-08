using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Content;

/// <summary>A node of the filter of a <see cref="ContentQuery"/>. The REST grammar, GraphQL, MCP and the typed builder all produce this tree.</summary>
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

/// <summary>A condition on one field.</summary>
/// <param name="Path">The dotted path of the field in the API shape, such as <c>title</c>, <c>seo.metaTitle</c>, <c>author.name</c>, <c>metadata.audience</c> or <c>sections.hero.heading</c>.</param>
/// <param name="Operator">The operator.</param>
/// <param name="Value">The operand: a single value; a list of values for <see cref="FilterOperator.In"/> and <see cref="FilterOperator.NotIn"/>; a list of two for <see cref="FilterOperator.Between"/>; <see langword="null"/> for <see cref="FilterOperator.IsNull"/> and <see cref="FilterOperator.IsNotNull"/>. The field type coerces it, so text is accepted for every field type.</param>
public sealed record FieldFilter(string Path, FilterOperator Operator, object? Value = null) : ContentFilter
{
    /// <summary>Whether two filters name the same path and operator and have equal operands, comparing a list of operands item by item.</summary>
    /// <param name="other">The other filter.</param>
    /// <returns><see langword="true"/> if the filters are equal.</returns>
    public bool Equals(FieldFilter? other) =>
        other is not null && Path == other.Path && Operator == other.Operator && SameOperand(Value, other.Value);

    /// <summary>Gets a hash code of the path and the operator.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Path, Operator);

    private static bool SameOperand(object? left, object? right) =>
        left is IEnumerable leftItems and not string && right is IEnumerable rightItems and not string
            ? leftItems.Cast<object?>().SequenceEqual(rightItems.Cast<object?>())
            : Equals(left, right);
}

/// <summary>Every condition holds.</summary>
/// <param name="Filters">The conditions.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "And is the name the specification gives the node.")]
public sealed record And(params IReadOnlyList<ContentFilter> Filters) : ContentFilter
{
    /// <summary>Whether two nodes have equal conditions in the same order.</summary>
    /// <param name="other">The other node.</param>
    /// <returns><see langword="true"/> if the nodes are equal.</returns>
    public bool Equals(And? other) => other is not null && Filters.SequenceEqual(other.Filters);

    /// <summary>Gets a hash code of the conditions in order.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashOf(Filters);
}

/// <summary>At least one condition holds.</summary>
/// <param name="Filters">The conditions.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Or is the name the specification gives the node.")]
public sealed record Or(params IReadOnlyList<ContentFilter> Filters) : ContentFilter
{
    /// <summary>Whether two nodes have equal conditions in the same order.</summary>
    /// <param name="other">The other node.</param>
    /// <returns><see langword="true"/> if the nodes are equal.</returns>
    public bool Equals(Or? other) => other is not null && Filters.SequenceEqual(other.Filters);

    /// <summary>Gets a hash code of the conditions in order.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashOf(Filters);
}

/// <summary>The condition does not hold.</summary>
/// <param name="Filter">The condition.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Not is the name the specification gives the node.")]
public sealed record Not(ContentFilter Filter) : ContentFilter;
