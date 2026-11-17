using System.Collections;
using FluentCMS.Admin.Lists;

namespace FluentCMS.Admin.Content;

internal abstract record ContentFilter
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

// The value is a string, a boolean, a number, a moment, a day or a Guid, or a list of them for in, notIn and between.
internal sealed record FieldFilter(string Path, FilterOperator Operator, object? Value = null) : ContentFilter
{
    public bool Equals(FieldFilter? other) =>
        other is not null && Path == other.Path && Operator == other.Operator && SameOperand(Value, other.Value);

    public override int GetHashCode() => HashCode.Combine(Path, Operator);

    private static bool SameOperand(object? left, object? right) =>
        left is IEnumerable leftItems and not string && right is IEnumerable rightItems and not string
            ? leftItems.Cast<object?>().SequenceEqual(rightItems.Cast<object?>())
            : Equals(left, right);
}

internal sealed record And(params IReadOnlyList<ContentFilter> Filters) : ContentFilter
{
    public bool Equals(And? other) => other is not null && Filters.SequenceEqual(other.Filters);

    public override int GetHashCode() => HashOf(Filters);
}

internal sealed record Or(params IReadOnlyList<ContentFilter> Filters) : ContentFilter
{
    public bool Equals(Or? other) => other is not null && Filters.SequenceEqual(other.Filters);

    public override int GetHashCode() => HashOf(Filters);
}

internal sealed record Not(ContentFilter Filter) : ContentFilter;

internal sealed record SortField(string Path, bool Descending = false);
