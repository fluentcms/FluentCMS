namespace FluentCMS.Schema;

/// <summary>Includes the field in full-text search.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SearchableAttribute : Attribute;
