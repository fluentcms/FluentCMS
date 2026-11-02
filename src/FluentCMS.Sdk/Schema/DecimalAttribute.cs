namespace FluentCMS.Schema;

/// <summary>Marks a property as a decimal number field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DecimalAttribute() : FieldAttribute("decimal")
{
    /// <summary>The number of significant digits. 0 leaves it to the database provider.</summary>
    public int Precision { get; set; }

    /// <summary>The number of digits after the decimal point. Used only together with <see cref="Precision"/>.</summary>
    public int Scale { get; set; }
}
