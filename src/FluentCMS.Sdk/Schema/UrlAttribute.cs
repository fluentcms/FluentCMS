namespace FluentCMS.Schema;

/// <summary>Marks a property as a URL field.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UrlAttribute() : FieldAttribute("url");
