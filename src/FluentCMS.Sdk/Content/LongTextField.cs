namespace FluentCMS.Content;

/// <summary>A field of the <c>longText</c> type, which can be searched for text but not compared or sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class LongTextField(string path) : QueryField(path)
{
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
}
