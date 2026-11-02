namespace FluentCMS.Content;

/// <summary>An operator a filter can apply to a field. The names in the REST grammar are the camelCase operators of the content query model.</summary>
public enum FilterOperator
{
    /// <summary>The value equals the operand (<c>eq</c>).</summary>
    Equal,

    /// <summary>The value differs from the operand (<c>ne</c>).</summary>
    NotEqual,

    /// <summary>The value is one of the operands (<c>in</c>).</summary>
    In,

    /// <summary>The value is none of the operands (<c>nin</c>).</summary>
    NotIn,

    /// <summary>The value is less than the operand (<c>lt</c>).</summary>
    LessThan,

    /// <summary>The value is less than or equal to the operand (<c>lte</c>).</summary>
    LessThanOrEqual,

    /// <summary>The value is greater than the operand (<c>gt</c>).</summary>
    GreaterThan,

    /// <summary>The value is greater than or equal to the operand (<c>gte</c>).</summary>
    GreaterThanOrEqual,

    /// <summary>The value lies between two operands, both included (<c>between</c>).</summary>
    Between,

    /// <summary>The value contains the operand: a substring of text, or a member of a list (<c>contains</c>).</summary>
    Contains,

    /// <summary>The text starts with the operand (<c>startsWith</c>).</summary>
    StartsWith,

    /// <summary>The field has no value (<c>null</c>).</summary>
    IsNull,

    /// <summary>The field has a value (<c>notNull</c>).</summary>
    IsNotNull,

    /// <summary>The block list holds a block of the given type (<c>hasBlock</c>).</summary>
    HasBlock,
}
