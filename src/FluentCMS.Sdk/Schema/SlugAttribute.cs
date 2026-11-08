namespace FluentCMS.Schema;

/// <summary>Marks a property as a slug field: lower case letters and digits of any script separated by hyphens, generated from another field unless set by hand.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SlugAttribute() : FieldAttribute("slug")
{
    /// <summary>The name of the property the slug is generated from, for example <c>nameof(Title)</c>.</summary>
    public string? Source { get; set; }
}
