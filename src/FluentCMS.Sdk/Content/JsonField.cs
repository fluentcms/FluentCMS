namespace FluentCMS.Content;

/// <summary>A field of the <c>json</c> type. A filter tests it for a value or reaches one key inside it with <see cref="Key"/>; it cannot be sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class JsonField(string path) : QueryField(path)
{
    /// <summary>A key one level inside the JSON, such as <c>audience</c> in <c>metadata.audience</c>.</summary>
    /// <param name="name">The key: ASCII letters, digits and underscores.</param>
    /// <returns>The key as a field.</returns>
    public JsonKeyField Key(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? new JsonKeyField($"{Path}.{name}")
            : throw new ArgumentException("A filter reaches one key inside JSON, named with ASCII letters, digits and underscores.", nameof(name));
    }

    /// <summary>The field has no value (<c>null</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field has a value (<c>notNull</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}
