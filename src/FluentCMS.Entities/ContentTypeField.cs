namespace FluentCMS.Entities;

/// <summary>
/// Represents a field within a content type, including properties for identification,
/// description, and configuration options like placeholders and default values.
/// </summary>
public class ContentTypeField
{
    /// <summary>
    /// The unique name of the field. This field won't be updated in the future.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Title of the field.
    /// </summary>
    public string Title { get; set; } = default!;

    /// <summary>
    /// Description of the field.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Label for the field, used in UI forms.
    /// </summary>
    public string Label { get; set; } = default!;

    /// <summary>
    /// Placeholder text for the field, can be null.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Hint or help text for the field, can be null.
    /// </summary>
    public string? Hint { get; set; }

    /// <summary>
    /// Default value of the field, can be null.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Indicates whether the field is required.
    /// </summary>
    public bool IsRequired { get; set; }
}
