namespace FluentCMS.Content;

/// <summary>A field of the <c>relation</c> type, single or multiple, filtered by the ids of its targets. It cannot be sorted.</summary>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class RelationField(string path) : QueryField(path)
{
    /// <summary>The field points to the entry (<c>eq</c>); a multiple relation holds it among others.</summary>
    /// <param name="id">The id of the entry.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(Guid id) => Filter(FilterOperator.Equal, id);

    /// <summary>The field points to one of the entries (<c>in</c>).</summary>
    /// <param name="ids">The ids of the entries.</param>
    /// <returns>The filter.</returns>
    public FieldFilter In(params IReadOnlyList<Guid> ids) => Filter(FilterOperator.In, List(ids));

    /// <summary>The field points to no entry (<c>null</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNull() => Filter(FilterOperator.IsNull);

    /// <summary>The field points to an entry (<c>notNull</c>).</summary>
    /// <returns>The filter.</returns>
    public FieldFilter IsNotNull() => Filter(FilterOperator.IsNotNull);
}
