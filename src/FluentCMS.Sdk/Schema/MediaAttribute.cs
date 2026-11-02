namespace FluentCMS.Schema;

/// <summary>Marks a property as a media field, which holds a reference to one or more media assets.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MediaAttribute() : FieldAttribute("media");
