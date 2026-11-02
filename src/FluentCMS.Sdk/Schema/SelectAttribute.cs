namespace FluentCMS.Schema;

/// <summary>
/// Marks a property as a select field. The property is an enum, or a list of an enum for a multi select; its
/// members are the options. Any other property lists its options in the attribute.
/// </summary>
/// <param name="options">The allowed values, for a property that is not an enum.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SelectAttribute(params string[] options) : FieldAttribute("select")
{
    /// <summary>The allowed values given in the attribute; empty for an enum.</summary>
    public IReadOnlyList<string> Options { get; } = options;
}
