namespace FluentCMS.Content;

/// <summary>A field of the <c>integer</c> or <c>decimal</c> type, which can be compared and sorted.</summary>
/// <typeparam name="T">The type of an operand: <see cref="long"/> for <c>integer</c>, <see cref="decimal"/> for <c>decimal</c>.</typeparam>
/// <param name="path">The dotted path of the field in the API shape.</param>
public sealed class NumberField<T>(string path) : QueryField(path)
    where T : struct
{
    /// <summary>The value equals the operand (<c>eq</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Eq(T value) => Filter(FilterOperator.Equal, value);

    /// <summary>The value differs from the operand (<c>ne</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Ne(T value) => Filter(FilterOperator.NotEqual, value);

    /// <summary>The value is one of the operands (<c>in</c>).</summary>
    /// <param name="values">The operands.</param>
    /// <returns>The filter.</returns>
    public FieldFilter In(params IReadOnlyList<T> values) => Filter(FilterOperator.In, List(values));

    /// <summary>The value is none of the operands (<c>nin</c>).</summary>
    /// <param name="values">The operands.</param>
    /// <returns>The filter.</returns>
    public FieldFilter NotIn(params IReadOnlyList<T> values) => Filter(FilterOperator.NotIn, List(values));

    /// <summary>The value is less than the operand (<c>lt</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Lt(T value) => Filter(FilterOperator.LessThan, value);

    /// <summary>The value is less than or equal to the operand (<c>lte</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Lte(T value) => Filter(FilterOperator.LessThanOrEqual, value);

    /// <summary>The value is greater than the operand (<c>gt</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Gt(T value) => Filter(FilterOperator.GreaterThan, value);

    /// <summary>The value is greater than or equal to the operand (<c>gte</c>).</summary>
    /// <param name="value">The operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Gte(T value) => Filter(FilterOperator.GreaterThanOrEqual, value);

    /// <summary>The value lies between the operands, both included (<c>between</c>).</summary>
    /// <param name="from">The lower operand.</param>
    /// <param name="to">The upper operand.</param>
    /// <returns>The filter.</returns>
    public FieldFilter Between(T from, T to) => Filter(FilterOperator.Between, new[] { from, to });

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
