namespace FluentCMS.Schema;

/// <summary>Leaves the field out of responses and forms.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class HiddenAttribute : Attribute;
