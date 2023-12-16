namespace FluentCMS.Entities;

/// <summary>
/// Represents a content type with properties for identification, description,
/// and associated content type fields.
/// Inherits from <see cref="AuditEntity" /> for audit tracking.
/// </summary>
public class ContentType : AuditEntity
{
    /// <summary>
    /// Title of the content type.
    /// </summary>
    public string Title { get; set; } = default!;

    /// <summary>
    /// Description of the content type.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The unique name of the content type. This field won't be updated in the future.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// List of fields associated with the content type.
    /// </summary>
    public List<ContentTypeField> Fields { get; set; } = [];
}
