namespace FluentCMS.Content;

/// <summary>
/// A field one level inside a block list, such as <c>sections.gallery.columns</c>, of the <c>integer</c>,
/// <c>decimal</c>, <c>boolean</c>, <c>date</c> or <c>dateTime</c> type. A filter holds when a block of that type in
/// the list matches. Inside a block, a value is compared for equality only.
/// </summary>
/// <typeparam name="T">The type of an operand: <see cref="long"/>, <see cref="decimal"/>, <see cref="bool"/>, <see cref="DateOnly"/> or <see cref="DateTimeOffset"/>.</typeparam>
/// <param name="path">The dotted path of the field, as the field, the block type and the field of the block.</param>
public sealed class BlockValueField<T>(string path) : QueryField(path)
    where T : struct
{
    /// <summary>A block holds the value (<c>eq</c>).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(T value) => Filter(FilterOperator.Equal, value);

    /// <summary>No block holds the value (<c>ne</c>).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(T value) => Filter(FilterOperator.NotEqual, value);
}
