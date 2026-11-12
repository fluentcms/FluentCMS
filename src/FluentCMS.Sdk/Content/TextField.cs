namespace FluentCMS.Content;

/// <summary>A field of the <c>text</c>, <c>slug</c>, <c>email</c> or <c>url</c> type, which can be filtered and sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class TextField(string path) : QueryField(path)
{
    /// <summary>The value equals the text (<c>eq</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(string value) => Filter(FilterOperator.Equal, Text(value));

    /// <summary>The value differs from the text (<c>ne</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(string value) => Filter(FilterOperator.NotEqual, Text(value));

    /// <summary>The value is one of the texts (<c>in</c>).</summary>
    /// <param name="values">The texts.</param>
    /// <returns>The filter.</returns>
    public FieldFilter In(params IReadOnlyList<string> values) => Filter(FilterOperator.In, List(values));

    /// <summary>The value is none of the texts (<c>nin</c>).</summary>
    /// <param name="values">The texts.</param>
    /// <returns>The filter.</returns>
    public FieldFilter NotIn(params IReadOnlyList<string> values) => Filter(FilterOperator.NotIn, List(values));

    /// <summary>The value contains the text (<c>contains</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));

    /// <summary>The value starts with the text (<c>startsWith</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter StartsWith(string value) => Filter(FilterOperator.StartsWith, Text(value));

    /// <summary>The field has no value (<c>null</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field has a value (<c>notNull</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    /// <summary>Sorts by the field, smallest first.</summary>
    /// <returns>The sort key.</returns>
    public SortField Ascending() => new(Path);

    /// <summary>Sorts by the field, largest first.</summary>
    /// <returns>The sort key.</returns>
    public SortField Descending() => new(Path, Descending: true);
}
