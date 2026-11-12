namespace FluentCMS.Content;

/// <summary>A key one level inside a <c>json</c> field, such as <c>metadata.audience</c>, which a filter compares with text, a number or a boolean.</summary>
/// <param name="path">The dotted path of the key in the API shape.</param>
public sealed class JsonKeyField(string path) : QueryField(path)
{
    /// <summary>The key holds the text (<c>eq</c>). Text that reads as a number or a boolean also matches that value.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(string value) => Filter(FilterOperator.Equal, Text(value));

    /// <summary>The key holds the number (<c>eq</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(long value) => Filter(FilterOperator.Equal, value);

    /// <summary>The key holds the number (<c>eq</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(decimal value) => Filter(FilterOperator.Equal, value);

    /// <summary>The key holds the boolean (<c>eq</c>).</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(bool value) => Filter(FilterOperator.Equal, value);

    /// <summary>The key does not hold the text (<c>ne</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(string value) => Filter(FilterOperator.NotEqual, Text(value));

    /// <summary>The key does not hold the number (<c>ne</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(long value) => Filter(FilterOperator.NotEqual, value);

    /// <summary>The key does not hold the number (<c>ne</c>).</summary>
    /// <param name="value">The number.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(decimal value) => Filter(FilterOperator.NotEqual, value);

    /// <summary>The key does not hold the boolean (<c>ne</c>).</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(bool value) => Filter(FilterOperator.NotEqual, value);

    /// <summary>The key holds text that contains the text, or a list that holds it (<c>contains</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));
}
