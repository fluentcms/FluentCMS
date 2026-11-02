namespace FluentCMS.Schema;

/// <summary>Makes the field read-only for clients.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ReadOnlyAttribute : Attribute;
