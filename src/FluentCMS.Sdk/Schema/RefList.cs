using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace FluentCMS.Schema;

/// <summary>
/// An ordered, immutable list of references to entries of content type <typeparamref name="T"/>.
/// Two lists are equal when they hold the same ids in the same order.
/// </summary>
/// <typeparam name="T">The content type of the referenced entries.</typeparam>
[CollectionBuilder(typeof(RefList), nameof(RefList.Create))]
public sealed record RefList<T> : IReadOnlyList<Ref<T>>
    where T : ContentItem
{
    private readonly Ref<T>[] _items;

    /// <summary>Creates an empty list.</summary>
    public RefList()
    {
        _items = [];
    }

    /// <summary>Creates a list from the given references.</summary>
    /// <param name="items">The references, in order.</param>
    public RefList(IEnumerable<Ref<T>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    internal RefList(ReadOnlySpan<Ref<T>> items)
    {
        _items = items.ToArray();
    }

    /// <summary>The number of references.</summary>
    public int Count => _items.Length;

    /// <summary>Gets the reference at an index.</summary>
    /// <param name="index">The zero-based index.</param>
    public Ref<T> this[int index] => _items[index];

    /// <inheritdoc/>
    public bool Equals(RefList<T>? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public IEnumerator<Ref<T>> GetEnumerator() => ((IEnumerable<Ref<T>>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Count = ").Append(_items.Length);
        return true;
    }
}

/// <summary>Builds <see cref="RefList{T}"/> from collection expressions.</summary>
public static class RefList
{
    /// <summary>Creates a list from the given references.</summary>
    /// <typeparam name="T">The content type of the referenced entries.</typeparam>
    /// <param name="items">The references, in order.</param>
    /// <returns>The new list.</returns>
    public static RefList<T> Create<T>(ReadOnlySpan<Ref<T>> items)
        where T : ContentItem => new(items);
}
