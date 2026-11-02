namespace FluentCMS.Schema;

/// <summary>Requires the value to be unique among published entries.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class UniqueAttribute : Attribute;
