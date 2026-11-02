namespace FluentCMS.Schema;

/// <summary>Marks a property as a date and time field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DateTimeAttribute() : FieldAttribute("dateTime");
