namespace FluentCMS.Schema;

/// <summary>Marks a property as an email address field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class EmailAttribute() : FieldAttribute("email");
