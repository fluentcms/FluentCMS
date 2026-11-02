namespace FluentCMS.Schema;

/// <summary>Marks a property as a true or false field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BooleanAttribute() : FieldAttribute("boolean");
