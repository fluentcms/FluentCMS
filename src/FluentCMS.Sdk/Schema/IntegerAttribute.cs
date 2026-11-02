namespace FluentCMS.Schema;

/// <summary>Marks a property as a whole number field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IntegerAttribute() : FieldAttribute("integer");
