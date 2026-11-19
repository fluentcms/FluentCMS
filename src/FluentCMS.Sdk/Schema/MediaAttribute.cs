using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Schema;

/// <summary>Marks a property as a media field, which holds a reference to one or more media assets.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MediaAttribute() : FieldAttribute("media")
{
    /// <summary>
    /// The types of asset the field takes, each a MIME type such as <c>image/png</c> or a family such as <c>image/*</c>;
    /// none for every type. Stored as the setting <see cref="FieldSettings.Accept"/>.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "A named argument of an attribute is an array.")]
    public string[]? Accept { get; set; }
}
