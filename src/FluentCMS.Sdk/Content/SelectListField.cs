namespace FluentCMS.Content;

/// <summary>A field of the <c>select</c> type that holds a list of options. It is stored as JSON, so it cannot be sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class SelectListField(string path) : QueryField(path)
{
    /// <summary>The list holds the option (<c>contains</c>).</summary>
    /// <param name="option">The option.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Contains(string option) => Filter(FilterOperator.Contains, Text(option));

    /// <summary>The list holds at least one of the options (<c>in</c>).</summary>
    /// <param name="options">The options.</param>
    /// <returns>The filter.</returns>
    public FieldFilter In(params IReadOnlyList<string> options) => Filter(FilterOperator.In, List(options));

    /// <summary>The list holds none of the options (<c>nin</c>).</summary>
    /// <param name="options">The options.</param>
    /// <returns>The filter.</returns>
    public FieldFilter NotIn(params IReadOnlyList<string> options) => Filter(FilterOperator.NotIn, List(options));

    /// <summary>The field has no value (<c>null</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field has a value (<c>notNull</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}
