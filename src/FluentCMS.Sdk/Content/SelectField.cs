namespace FluentCMS.Content;

/// <summary>
/// A field of the <c>select</c> type that holds one option, which can be filtered and sorted. An option is the value
/// the API shows, such as <c>video</c> for an enum member <c>Video</c>.
/// </summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class SelectField(string path) : QueryField(path)
{
    /// <summary>The value is the option (<c>eq</c>).</summary>
    /// <param name="option">The option.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(string option) => Filter(FilterOperator.Equal, Text(option));

    /// <summary>The value is not the option (<c>ne</c>).</summary>
    /// <param name="option">The option.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(string option) => Filter(FilterOperator.NotEqual, Text(option));

    /// <summary>The value is one of the options (<c>in</c>).</summary>
    /// <param name="options">The options.</param>
    /// <returns>The filter.</returns>
    public FieldFilter In(params IReadOnlyList<string> options) => Filter(FilterOperator.In, List(options));

    /// <summary>The value is none of the options (<c>nin</c>).</summary>
    /// <param name="options">The options.</param>
    /// <returns>The filter.</returns>
    public FieldFilter NotIn(params IReadOnlyList<string> options) => Filter(FilterOperator.NotIn, List(options));

    /// <summary>The value contains the text (<c>contains</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));

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
