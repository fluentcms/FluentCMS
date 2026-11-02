namespace FluentCMS.Schema;

/// <summary>Marks a property as a rich text field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RichTextAttribute() : FieldAttribute("richText");
