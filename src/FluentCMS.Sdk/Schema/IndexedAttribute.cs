namespace FluentCMS.Schema;

/// <summary>Gives the field a database index.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IndexedAttribute : Attribute;
