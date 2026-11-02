using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace FluentCMS.Schema;

/// <summary>
/// An ordered, immutable list of blocks. Each block is an instance of a class marked as a block type.
/// Two lists are equal when their blocks are equal, in order, by <see cref="object.Equals(object?)"/>.
/// </summary>
[CollectionBuilder(typeof(BlockList), nameof(Create))]
public sealed record BlockList : IReadOnlyList<object>
{
    private readonly object[] _items;

    /// <summary>Creates an empty list.</summary>
    public BlockList()
    {
        _items = [];
    }

    /// <summary>Creates a list from the given blocks.</summary>
    /// <param name="items">The blocks, in order. None may be <see langword="null"/>.</param>
    public BlockList(IEnumerable<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        ThrowIfAnyNull(_items);
    }

    private BlockList(ReadOnlySpan<object> items)
    {
        _items = items.ToArray();
        ThrowIfAnyNull(_items);
    }

    /// <summary>The number of blocks.</summary>
    public int Count => _items.Length;

    /// <summary>Gets the block at an index.</summary>
    /// <param name="index">The zero-based index.</param>
    public object this[int index] => _items[index];

    /// <summary>Creates a list from the given blocks.</summary>
    /// <param name="items">The blocks, in order. None may be <see langword="null"/>.</param>
    /// <returns>The new list.</returns>
    public static BlockList Create(ReadOnlySpan<object> items) => new(items);

    /// <inheritdoc/>
    public bool Equals(BlockList? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

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
    public IEnumerator<object> GetEnumerator() => ((IEnumerable<object>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Count = ").Append(_items.Length);
        return true;
    }

    private static void ThrowIfAnyNull(object?[] items)
    {
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));
        }
    }
}
