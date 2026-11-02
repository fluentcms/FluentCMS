using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FluentCMS.Content;

/// <summary>
/// An entry of any content type as an ordered dictionary of values by API field name, with typed accessors.
/// The Management API, MCP and import work with it, and a hook for <see cref="ContentDocument"/> sees every type.
/// Names are case-sensitive and keep the order in which they were added.
/// </summary>
[SuppressMessage("Naming", "CA1710:Identifier should have correct suffix", Justification = "ContentDocument is the name the specification gives the type.")]
public sealed class ContentDocument : IDictionary<string, object?>, IReadOnlyDictionary<string, object?>
{
    private readonly OrderedDictionary<string, object?> _values = new(StringComparer.Ordinal);

    /// <summary>Creates an empty document.</summary>
    public ContentDocument()
    {
    }

    /// <summary>Creates a document with values.</summary>
    /// <param name="values">The values, in order.</param>
    public ContentDocument(IEnumerable<KeyValuePair<string, object?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (name, value) in values)
        {
            _values[name] = value;
        }
    }

    /// <summary>The number of values.</summary>
    public int Count => _values.Count;

    /// <summary>The field names, in order.</summary>
    public ICollection<string> Keys => _values.Keys;

    /// <summary>The values, in order.</summary>
    public ICollection<object?> Values => _values.Values;

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => _values.Keys;

    IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => _values.Values;

    /// <summary>Gets or sets a value. Setting a new name appends it; setting an existing name keeps its place.</summary>
    /// <param name="name">The API field name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="KeyNotFoundException">There is no such field when reading.</exception>
    public object? this[string name]
    {
        get => _values[name];
        set => _values[name] = value;
    }

    /// <summary>Gets a value as a type.</summary>
    /// <typeparam name="T">The type to read the value as. A number converts to another numeric type.</typeparam>
    /// <param name="name">The API field name.</param>
    /// <returns>The value, or the default of <typeparamref name="T"/> when the field is missing or null.</returns>
    /// <exception cref="InvalidCastException">The value does not convert to <typeparamref name="T"/>.</exception>
    public T? Get<T>(string name) => TryGet<T>(name, out var value) ? value : default;

    /// <summary>Tries to get a value as a type.</summary>
    /// <typeparam name="T">The type to read the value as. A number converts to another numeric type.</typeparam>
    /// <param name="name">The API field name.</param>
    /// <param name="value">The value, or the default of <typeparamref name="T"/> when the field is missing or null.</param>
    /// <returns><see langword="true"/> when the field exists and holds a non-null value.</returns>
    /// <exception cref="InvalidCastException">The value does not convert to <typeparamref name="T"/>.</exception>
    public bool TryGet<T>(string name, out T? value)
    {
        if (!_values.TryGetValue(name, out var raw) || raw is null)
        {
            value = default;
            return false;
        }

        value = Convert<T>(raw);
        return true;
    }

    /// <summary>Adds a value.</summary>
    /// <param name="name">The API field name.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The field already exists.</exception>
    public void Add(string name, object? value) => _values.Add(name, value);

    /// <summary>Whether the document has a field.</summary>
    /// <param name="name">The API field name.</param>
    /// <returns><see langword="true"/> when the field exists, even with a null value.</returns>
    public bool ContainsKey(string name) => _values.ContainsKey(name);

    /// <summary>Gets a value.</summary>
    /// <param name="name">The API field name.</param>
    /// <param name="value">The value, when the field exists.</param>
    /// <returns><see langword="true"/> when the field exists.</returns>
    public bool TryGetValue(string name, [MaybeNullWhen(false)] out object? value) => _values.TryGetValue(name, out value);

    /// <summary>Removes a field.</summary>
    /// <param name="name">The API field name.</param>
    /// <returns><see langword="true"/> when the field existed.</returns>
    public bool Remove(string name) => _values.Remove(name);

    /// <summary>Removes every field.</summary>
    public void Clear() => _values.Clear();

    /// <summary>Gets the values in order.</summary>
    /// <returns>An enumerator over the name and value pairs.</returns>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) => _values.Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item) =>
        _values.TryGetValue(item.Key, out var value) && Equals(value, item.Value);

    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)this).Contains(item) && _values.Remove(item.Key);

    private static T? Convert<T>(object raw)
    {
        if (raw is T typed)
        {
            return typed;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (raw is IConvertible && typeof(IConvertible).IsAssignableFrom(target) && !target.IsEnum)
        {
            try
            {
                return (T)System.Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new InvalidCastException($"The value {raw} does not convert to {typeof(T)}.", exception);
            }
        }

        throw new InvalidCastException($"A value of type {raw.GetType()} does not convert to {typeof(T)}.");
    }
}
