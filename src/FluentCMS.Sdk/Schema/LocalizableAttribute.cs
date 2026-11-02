namespace FluentCMS.Schema;

/// <summary>Gives the field a value per locale. The type must be localized.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LocalizableAttribute : Attribute;
