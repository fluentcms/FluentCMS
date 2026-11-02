using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace FluentCMS.Media;

/// <summary>
/// An ordered, immutable list of references to media assets.
/// Two lists are equal when they hold the same ids in the same order.
/// </summary>
[CollectionBuilder(typeof(MediaRefList), nameof(Create))]
public sealed record MediaRefList : IReadOnlyList<MediaRef>
{
    private readonly MediaRef[] _items;

    /// <summary>Creates an empty list.</summary>
    public MediaRefList()
    {
        _items = [];
    }

    /// <summary>Creates a list from the given references.</summary>
    /// <param name="items">The references, in order.</param>
    public MediaRefList(IEnumerable<MediaRef> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    private MediaRefList(ReadOnlySpan<MediaRef> items)
    {
        _items = items.ToArray();
    }

    /// <summary>The number of references.</summary>
    public int Count => _items.Length;

    /// <summary>Gets the reference at an index.</summary>
    /// <param name="index">The zero-based index.</param>
    public MediaRef this[int index] => _items[index];

    /// <summary>Creates a list from the given references.</summary>
    /// <param name="items">The references, in order.</param>
    /// <returns>The new list.</returns>
    public static MediaRefList Create(ReadOnlySpan<MediaRef> items) => new(items);

    /// <inheritdoc/>
    public bool Equals(MediaRefList? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

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
    public IEnumerator<MediaRef> GetEnumerator() => ((IEnumerable<MediaRef>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Count = ").Append(_items.Length);
        return true;
    }
}
