namespace FluentCMS.Admin.Lists;

/// <summary>An operator of the query model's filters, which a list filter compares a field with.</summary>
public enum FilterOperator
{
    /// <summary>The value is the operand: <c>eq</c>.</summary>
    Equal,

    /// <summary>The value is not the operand: <c>ne</c>.</summary>
    NotEqual,

    /// <summary>The value is one of the operands: <c>in</c>.</summary>
    In,

    /// <summary>The value is none of the operands: <c>nin</c>.</summary>
    NotIn,

    /// <summary>The value is less than the operand: <c>lt</c>.</summary>
    LessThan,

    /// <summary>The value is at most the operand: <c>lte</c>.</summary>
    LessThanOrEqual,

    /// <summary>The value is greater than the operand: <c>gt</c>.</summary>
    GreaterThan,

    /// <summary>The value is at least the operand: <c>gte</c>.</summary>
    GreaterThanOrEqual,

    /// <summary>The value is between two operands, both included: <c>between</c>.</summary>
    Between,

    /// <summary>The text contains the operand, or a list holds it: <c>contains</c>.</summary>
    Contains,

    /// <summary>The text starts with the operand: <c>startsWith</c>.</summary>
    StartsWith,

    /// <summary>The field has no value: <c>null</c>.</summary>
    IsNull,

    /// <summary>The field has a value: <c>notNull</c>.</summary>
    IsNotNull,

    /// <summary>A blocks field holds a block of the type the operand names: <c>hasBlock</c>.</summary>
    HasBlock,
}
