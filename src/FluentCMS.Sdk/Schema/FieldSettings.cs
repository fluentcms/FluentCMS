namespace FluentCMS.Schema;

/// <summary>The keys of <see cref="SchemaField.Settings"/> that the built-in field types use.</summary>
public static class FieldSettings
{
    /// <summary>For a slug field, the name of the field the slug is generated from. A string.</summary>
    public const string Source = "source";

    /// <summary>For a select field, the allowed values. A list of strings.</summary>
    public const string Options = "options";

    /// <summary>For a component field, the name of the component. A string.</summary>
    public const string Component = "component";

    /// <summary>For a blocks field, the names of the allowed blocks. A list of strings.</summary>
    public const string Blocks = "blocks";

    /// <summary>For a decimal field, the number of significant digits. A whole number.</summary>
    public const string Precision = "precision";

    /// <summary>For a decimal field, the number of digits after the decimal point. A whole number.</summary>
    public const string Scale = "scale";
}
