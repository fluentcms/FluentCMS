namespace FluentCMS.Schema;

/// <summary>
/// The base of the attributes that turn a property into a field. A property has exactly one of them. A field
/// type that a plugin adds derives from it and passes the name it registers.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public abstract class FieldAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="fieldType">The name of the field type, such as <c>text</c>.</param>
    protected FieldAttribute(string fieldType)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldType);
        FieldType = fieldType;
    }

    /// <summary>The name of the field type.</summary>
    public string FieldType { get; }
}
