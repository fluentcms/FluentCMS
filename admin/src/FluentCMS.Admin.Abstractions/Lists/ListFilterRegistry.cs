using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Lists;

/// <summary>
/// The filters the generated lists offer, by field type: the operators of the query model each type takes in the admin, and
/// the operand they compare with. The core field types are in it from the start; an admin half names the operators of its
/// own field types with <see cref="Add"/>. A field type that is in neither is offered no filter.
/// </summary>
public sealed class ListFilterRegistry
{
    // What a field that holds a list takes: whether it holds a value, or any of some values.
    private static readonly FilterOperator[] ListOperators = [FilterOperator.Contains, FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsNull, FilterOperator.IsNotNull];

    private readonly Dictionary<string, ListFilterDefinition> _filters = new(StringComparer.Ordinal);

    /// <summary>Initializes a registry that holds the filters of the core field types.</summary>
    public ListFilterRegistry()
    {
        FilterOperator[] text = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsNull, FilterOperator.IsNotNull];
        FilterOperator[] number = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual, FilterOperator.Between, FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsNull, FilterOperator.IsNotNull];
        FilterOperator[] range = [FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual, FilterOperator.Between, FilterOperator.IsNull, FilterOperator.IsNotNull];
        FilterOperator[] presence = [FilterOperator.IsNull, FilterOperator.IsNotNull];

        foreach (var type in new[] { "text", "email", "url", "slug" })
        {
            Add(type, FilterOperand.Text, text);
        }

        Add("longText", FilterOperand.Text, FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.IsNull, FilterOperator.IsNotNull);
        Add("richText", FilterOperand.Text, presence);
        Add("json", FilterOperand.Text, presence);
        Add("integer", FilterOperand.WholeNumber, number);
        Add("decimal", FilterOperand.DecimalNumber, number);
        Add("date", FilterOperand.Date, [FilterOperator.Equal, FilterOperator.NotEqual, .. range]);
        Add("dateTime", FilterOperand.DateTime, range);
        Add("boolean", FilterOperand.Boolean, FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.IsNull, FilterOperator.IsNotNull);
        Add("select", FilterOperand.Option, FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsNull, FilterOperator.IsNotNull);
        Add("relation", FilterOperand.Id, FilterOperator.Equal, FilterOperator.In, FilterOperator.IsNull, FilterOperator.IsNotNull);
        Add("blocks", FilterOperand.Text, FilterOperator.HasBlock, FilterOperator.IsNull, FilterOperator.IsNotNull);
    }

    /// <summary>Names the operators of a field type, in place of those named before.</summary>
    /// <param name="fieldType">The field type, as <see cref="SchemaFieldDescription.Type"/> names it.</param>
    /// <param name="operand">What the operators compare with.</param>
    /// <param name="operators">The operators, in the order a filter offers them; the server must take each of them.</param>
    public void Add(string fieldType, FilterOperand operand, params IEnumerable<FilterOperator> operators)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldType);
        ArgumentNullException.ThrowIfNull(operators);
        var distinct = operators.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            throw new ArgumentException("A filter needs at least one operator.", nameof(operators));
        }

        _filters[fieldType] = new ListFilterDefinition(fieldType, operand, distinct);
    }

    /// <summary>
    /// The filter a list offers on a field: the one of its type, which a field that holds a list narrows to the operators
    /// that test its members. A hidden field has none, nor does one whose type names no operators.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The filter, or <see langword="null"/> when the field is offered none.</returns>
    public ListFilterDefinition? Find(SchemaFieldDescription field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Hidden || !_filters.TryGetValue(field.Type, out var filter))
        {
            return null;
        }

        if (!field.Multiple || field.Type == "relation" || field.Type == "blocks")
        {
            return filter;
        }

        var operators = ListOperators.Where(item => filter.Operators.Contains(item) || (item == FilterOperator.Contains && filter.Operators.Contains(FilterOperator.Equal))).ToArray();
        return operators.Length == 0 ? null : new ListFilterDefinition(filter.FieldType, filter.Operand, operators);
    }
}

/// <summary>The filter of one field type in a <see cref="ListFilterRegistry"/>.</summary>
public sealed class ListFilterDefinition
{
    internal ListFilterDefinition(string fieldType, FilterOperand operand, IReadOnlyList<FilterOperator> operators)
    {
        FieldType = fieldType;
        Operand = operand;
        Operators = operators;
    }

    /// <summary>Gets the field type.</summary>
    public string FieldType { get; }

    /// <summary>Gets what the operators compare with.</summary>
    public FilterOperand Operand { get; }

    /// <summary>Gets the operators, in the order a filter offers them.</summary>
    public IReadOnlyList<FilterOperator> Operators { get; }
}
