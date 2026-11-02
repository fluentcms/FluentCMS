namespace FluentCMS.Schema;

/// <summary>Requires a value to publish. A draft can be saved without one.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RequiredAttribute : Attribute;
