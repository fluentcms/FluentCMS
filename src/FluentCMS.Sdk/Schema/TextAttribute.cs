namespace FluentCMS.Schema;

/// <summary>Marks a property as a short text field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class TextAttribute() : FieldAttribute("text")
{
    /// <summary>The most number of characters, which also sizes the database column. 0 leaves it unlimited.</summary>
    public int MaxLength { get; set; }
}
