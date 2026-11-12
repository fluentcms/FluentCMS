namespace FluentCMS.Content;

/// <summary>A field of the <c>richText</c> type, which a filter can only test for a value.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class RichTextField(string path) : QueryField(path)
{
    /// <summary>The field has no value (<c>null</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field has a value (<c>notNull</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}
