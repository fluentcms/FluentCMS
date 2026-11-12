namespace FluentCMS.Content;

/// <summary>
/// A field one level inside a block list of the <c>longText</c> type, or a <c>select</c> that holds a list of options.
/// A filter holds when a block of that type in the list contains the text, or holds the option.
/// </summary>
/// <param name="path">The dotted path of the field, as the field, the block type and the field of the block.</param>
public sealed class BlockContainsField(string path) : QueryField(path)
{
    /// <summary>A block holds text that contains the text, or a list that holds it (<c>contains</c>).</summary>
    /// <param name="value">The text.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Contains(string value) => Filter(FilterOperator.Contains, Text(value));
}
