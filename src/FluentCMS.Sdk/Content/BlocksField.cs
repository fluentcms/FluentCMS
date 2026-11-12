namespace FluentCMS.Content;

/// <summary>
/// A field of the <c>blocks</c> type. A filter tests whether the list holds a block of a type; the builder a generator
/// emits for the field derives from this class and adds the block types of the field and their fields. It cannot be sorted.
/// </summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public class BlocksField(string path) : QueryField(path)
{
    /// <summary>The list holds a block of the type (<c>hasBlock</c>).</summary>
    /// <param name="blockType">The name of the block type, such as <c>hero</c>.</param>
    /// <returns>The filter.</returns>
    public FieldFilter HasBlock(string blockType)
    {
        ArgumentException.ThrowIfNullOrEmpty(blockType);
        return Filter(FilterOperator.HasBlock, blockType);
    }

    /// <summary>The field has no value (<c>null</c>): the list is missing or empty.</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field has a value (<c>notNull</c>): the list holds a block.</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}
