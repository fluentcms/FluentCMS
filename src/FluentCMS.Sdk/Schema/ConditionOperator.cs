namespace FluentCMS.Schema;

/// <summary>The operation a <see cref="Condition"/> performs.</summary>
public enum ConditionOperator
{
    /// <summary>The field equals a single value.</summary>
    Equal = 1,

    /// <summary>The field equals one of several values.</summary>
    In = 2,

    /// <summary>The field has no value.</summary>
    IsEmpty = 3,

    /// <summary>Every operand holds.</summary>
    And = 4,

    /// <summary>At least one operand holds.</summary>
    Or = 5,

    /// <summary>The single operand does not hold.</summary>
    Not = 6,
}
