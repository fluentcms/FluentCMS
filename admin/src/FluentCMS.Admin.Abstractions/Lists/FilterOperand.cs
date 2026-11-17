namespace FluentCMS.Admin.Lists;

/// <summary>What a list filter on a field type takes as its operand, which decides the input the filter shows.</summary>
public enum FilterOperand
{
    /// <summary>A text.</summary>
    Text,

    /// <summary>A whole number.</summary>
    WholeNumber,

    /// <summary>A decimal number.</summary>
    DecimalNumber,

    /// <summary>A day, without a time.</summary>
    Date,

    /// <summary>A moment, entered in the user's time zone and sent in UTC.</summary>
    DateTime,

    /// <summary>Yes or no.</summary>
    Boolean,

    /// <summary>One of the <c>options</c> in the settings of the field.</summary>
    Option,

    /// <summary>The id of an entry or an asset.</summary>
    Id,
}
