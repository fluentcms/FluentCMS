namespace FluentCMS.Schema;

/// <summary>Marks a property as a long text field: plain text without a short length limit.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LongTextAttribute() : FieldAttribute("longText");
