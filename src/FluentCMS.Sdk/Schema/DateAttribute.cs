namespace FluentCMS.Schema;

/// <summary>Marks a property as a date field, without a time.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DateAttribute() : FieldAttribute("date");
