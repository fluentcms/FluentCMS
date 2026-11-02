namespace FluentCMS.Schema;

/// <summary>Marks a property as a field that holds any JSON document.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class JsonAttribute() : FieldAttribute("json");
