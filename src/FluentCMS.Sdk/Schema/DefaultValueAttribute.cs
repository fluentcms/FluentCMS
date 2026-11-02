namespace FluentCMS.Schema;

/// <summary>Sets the value a new entry starts with.</summary>
/// <param name="value">A string, boolean, number, enum member or array of strings.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DefaultValueAttribute(object? value) : Attribute
{
    /// <summary>The value.</summary>
    public object? Value { get; } = value;
}
