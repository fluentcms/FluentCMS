namespace FluentCMS.Content;

/// <summary>The codes of the built-in validation rules, so plugins and clients can match on them.</summary>
public static class ValidationErrorCodes
{
    /// <summary>A required field has no value.</summary>
    public const string Required = "FCMS2101";

    /// <summary>The value is shorter than the field's minimum length, or a multiple field has too few items.</summary>
    public const string MinLength = "FCMS2102";

    /// <summary>The value is longer than the field's maximum length, or a multiple field has too many items.</summary>
    public const string MaxLength = "FCMS2103";

    /// <summary>The value is below the field's minimum.</summary>
    public const string BelowMinimum = "FCMS2104";

    /// <summary>The value is above the field's maximum.</summary>
    public const string AboveMaximum = "FCMS2105";

    /// <summary>The value does not match the field's regular expression.</summary>
    public const string PatternMismatch = "FCMS2106";

    /// <summary>The value is not one of the allowed options, or a block is of a type the field does not allow.</summary>
    public const string NotAnAllowedOption = "FCMS2107";

    /// <summary>The value cannot be read as the type of the field.</summary>
    public const string InvalidValue = "FCMS2108";

    /// <summary>A custom validator rejected the value without giving a code.</summary>
    public const string Custom = "FCMS2109";

    /// <summary>The field's <c>RequiredWhen</c> condition holds and the field has no value.</summary>
    public const string RequiredByCondition = "FCMS2110";

    /// <summary>Another entry in the same site and locale holds the value of a unique field, as a draft or published.</summary>
    public const string NotUnique = "FCMS2111";

    /// <summary>A required slug was to be made from its source field, whose text has no letter or digit to make it from.</summary>
    public const string EmptySlug = "FCMS2112";

    /// <summary>A reference names a target that does not exist in the site, such as a media asset that was never uploaded or is deleted.</summary>
    public const string UnknownReference = "FCMS2113";
}
