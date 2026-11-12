namespace FluentCMS.Content;

/// <summary>
/// A text field one level inside a block list, such as <c>sections.hero.heading</c>, of the <c>text</c>, <c>slug</c>,
/// <c>email</c>, <c>url</c> or single <c>select</c> type. A filter holds when a block of that type in the list matches.
/// Inside a block, text is compared and searched only.
/// </summary>
/// <param name="path">The dotted path of the field, as the field, the block type and the field of the block.</param>
public sealed class BlockTextField(string path) : QueryField(path)
{
    /// <summary>A block holds the text (<c>eq</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(string value) => Filter(FilterOperator.Equal, Text(value));

    /// <summary>No block holds the text (<c>ne</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(string value) => Filter(FilterOperator.NotEqual, Text(value));

    /// <summary>A block holds text that contains the text (<c>contains</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));
}
