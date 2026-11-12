namespace FluentCMS.Content;

/// <summary>A field of the <c>boolean</c> type, which can be filtered and sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class BooleanField(string path) : QueryField(path)
{
    /// <summary>The value equals the operand (<c>eq</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(bool value) => Filter(FilterOperator.Equal, value);

    /// <summary>The value differs from the operand (<c>ne</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(bool value) => Filter(FilterOperator.NotEqual, value);

    /// <summary>The field has no value (<c>null</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field has a value (<c>notNull</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);

    /// <summary>Sorts by the field, <see langword="false"/> first.</summary>
    /// <returns>The sort key.</returns>
    public SortField Ascending() => new(Path);

    /// <summary>Sorts by the field, <see langword="true"/> first.</summary>
    /// <returns>The sort key.</returns>
    public SortField Descending() => new(Path, Descending: true);
}
