using FluentCMS.Content;

namespace FluentCMS.Core.Schema.FieldTypes;

internal static class FilterSets
{
    public static readonly FilterOperator[] Text =
    [
        FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn,
        FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.IsNull, FilterOperator.IsNotNull,
    ];

    public static readonly FilterOperator[] Ordered =
    [
        FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.In, FilterOperator.NotIn,
        FilterOperator.LessThan, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
        FilterOperator.Between, FilterOperator.IsNull, FilterOperator.IsNotNull,
    ];

    public static readonly FilterOperator[] Equality = [FilterOperator.Equal, FilterOperator.NotEqual, FilterOperator.IsNull, FilterOperator.IsNotNull];

    public static readonly FilterOperator[] NullChecks = [FilterOperator.IsNull, FilterOperator.IsNotNull];
}
