using System.Collections;

namespace FluentCMS.Generators.Emit;

// An array compared by its items, so that the descriptions an incremental step returns compare equal when nothing changed.
internal readonly struct EquatableArray<T>(T[]? items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[] _items = items ?? [];

    public static EquatableArray<T> Empty { get; } = new([]);

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    // A default instance has no array.
    private T[] Items => _items ?? [];

    public bool Equals(EquatableArray<T> other)
    {
        var (left, right) = (Items, other.Items);
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = unchecked((hash * 31) + (item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item)));
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T> items)
        where T : IEquatable<T> => new([.. items]);
}
